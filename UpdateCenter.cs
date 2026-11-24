using System.IO;

namespace Zypora;

// 进程级更新协调器:启动时静默预下载,手动更新时复用同一任务,退出时可静默安装。
public sealed class UpdateCenter
{
    private readonly Func<CancellationToken, Task<UpdateInfo?>> _check;
    private readonly Func<UpdateInfo, string, IProgress<double>?, CancellationToken, Task<bool>> _download;
    private readonly Func<string, string> _tempDir;

    private readonly object _gate = new();
    private readonly List<IProgress<double>> _sinks = new();
    private bool _started;
    private bool _ready;
    private bool _installed;
    private UpdateInfo? _readyInfo;
    private string? _readyZip;
    private Task<bool>? _task;
    private UpdateInfo? _pendingInfo;

    public UpdateCenter(
        Func<CancellationToken, Task<UpdateInfo?>>? check = null,
        Func<UpdateInfo, string, IProgress<double>?, CancellationToken, Task<bool>>? download = null,
        Func<string, string>? tempDir = null)
    {
        _check = check ?? (ct => UpdateService.CheckAsync(null, ct));
        _download = download ?? ((info, dest, p, ct) => UpdateService.DownloadAsync(info, dest, p, ct));
        _tempDir = tempDir ?? UpdateService.TempDir;
    }

    public static UpdateCenter Shared { get; } = new();

    public event Action<UpdateInfo>? Ready;

    public bool IsReady
    {
        get { lock (_gate) return _ready; }
    }

    public UpdateInfo? ReadyInfo
    {
        get { lock (_gate) return _readyInfo; }
    }

    /// <summary>启动时静默检查 + 预下载;失败静默。重复调用只执行一次。</summary>
    public Task StartBackground(string currentVersion, AppSettings settings)
    {
        lock (_gate)
        {
            if (_started) return Task.CompletedTask;
            _started = true;
        }

        return Task.Run(async () =>
        {
            try
            {
                var info = await _check(CancellationToken.None).ConfigureAwait(false);
                if (info == null || !UpdateService.IsNewer(info.Version, currentVersion)) return;
                if (!string.IsNullOrWhiteSpace(settings.SkippedVersion) &&
                    string.Equals(info.Version, settings.SkippedVersion, StringComparison.OrdinalIgnoreCase)) return;
                await EnsureReadyAsync(info, null, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 静默
            }
        });
    }

    /// <summary>确保安装包就绪;下载中则复用同一任务并把进度转发给本次订阅。</summary>
    public Task<bool> EnsureReadyAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct)
    {
        Task<bool> task;
        lock (_gate)
        {
            if (_ready && _readyInfo != null && SameVersion(_readyInfo, info))
            {
                progress?.Report(1);
                return Task.FromResult(true);
            }

            if (_task == null || _pendingInfo == null || !SameVersion(_pendingInfo, info))
            {
                _pendingInfo = info;
                _task = Task.Run(() => DownloadCoreAsync(info));
            }
            task = _task;
            if (progress != null) _sinks.Add(progress);
        }

        return AwaitAndDetachAsync(task, progress, ct);
    }

    public void MarkInstalled()
    {
        lock (_gate) _installed = true;
    }

    /// <summary>正常退出时静默安装已就绪的更新;失败返回 false,不影响退出。</summary>
    public bool ApplyReadyAtExit(string appDir, string settingsDir)
    {
        UpdateInfo info;
        string zip;
        lock (_gate)
        {
            if (_installed || !_ready || _readyInfo == null || _readyZip == null) return false;
            _installed = true;
            info = _readyInfo;
            zip = _readyZip;
        }

        try
        {
            var extract = Path.Combine(_tempDir(info.Version), "extract");
            if (!UpdateService.ExtractUpdate(zip, extract, out _))
            {
                lock (_gate) _installed = false;
                return false;
            }

            var apply = UpdateService.ApplyUpdate(appDir, extract);
            if (!apply.Success)
            {
                lock (_gate) _installed = false;
                return false;
            }

            UpdateService.WritePendingInfo(settingsDir, info.Version, info.ChangelogOrNotes);
            return true;
        }
        catch
        {
            lock (_gate) _installed = false;
            return false;
        }
    }

    private async Task<bool> AwaitAndDetachAsync(Task<bool> task, IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (progress != null)
            {
                lock (_gate) _sinks.Remove(progress);
            }
        }
    }

    private async Task<bool> DownloadCoreAsync(UpdateInfo info)
    {
        try
        {
            var dir = _tempDir(info.Version);
            Directory.CreateDirectory(dir);
            var zip = Path.Combine(dir, UpdateService.ZipName);

            if (File.Exists(zip) &&
                (string.IsNullOrWhiteSpace(info.Sha256) || UpdateService.VerifySha256(zip, info.Sha256)))
            {
                Complete(info, zip);
                return true;
            }

            var ok = await _download(info, zip, new SinkProgress(this), CancellationToken.None).ConfigureAwait(false);
            if (!ok || !File.Exists(zip))
            {
                ResetPending();
                return false;
            }

            if (!string.IsNullOrWhiteSpace(info.Sha256) && !UpdateService.VerifySha256(zip, info.Sha256))
            {
                try { File.Delete(zip); } catch { }
                ResetPending();
                return false;
            }

            Complete(info, zip);
            return true;
        }
        catch
        {
            ResetPending();
            return false;
        }
    }

    private void Complete(UpdateInfo info, string zip)
    {
        lock (_gate)
        {
            _ready = true;
            _readyInfo = info;
            _readyZip = zip;
        }

        try { Ready?.Invoke(info); } catch { }
    }

    private void ResetPending()
    {
        lock (_gate)
        {
            _task = null;
            _pendingInfo = null;
        }
    }

    private void ReportProgress(double value)
    {
        IProgress<double>[] sinks;
        lock (_gate) sinks = _sinks.ToArray();
        foreach (var sink in sinks)
        {
            try { sink.Report(value); } catch { }
        }
    }

    private static bool SameVersion(UpdateInfo a, UpdateInfo b) =>
        string.Equals(a.Version, b.Version, StringComparison.OrdinalIgnoreCase);

    private sealed class SinkProgress : IProgress<double>
    {
        private readonly UpdateCenter _owner;
        public SinkProgress(UpdateCenter owner) => _owner = owner;
        public void Report(double value) => _owner.ReportProgress(value);
    }
}

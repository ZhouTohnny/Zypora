using System.IO;

namespace Zypora;

public enum ExternalFileDecision
{
    Ignore,
    Reload,
    Conflict,
}

public static class ExternalFileChange
{
    public static ExternalFileDecision Decide(string diskText, string currentText, bool dirty)
    {
        if (diskText == currentText) return ExternalFileDecision.Ignore;
        return dirty ? ExternalFileDecision.Conflict : ExternalFileDecision.Reload;
    }
}

public enum FileChangeKind
{
    Modified,
    Deleted,
}

public sealed class FileChangeMonitor : IDisposable
{
    private const int DebounceMs = 300;
    private const int MaxReadRetries = 3;

    private readonly Func<string?> _read;
    private readonly Action<FileChangeKind, string?> _onChanged;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _debounce;
    private FileSystemWatcher? _watcher;
    private string? _diskText;
    private int _retries;
    private bool _disposed;

    public FileChangeMonitor(string path, Func<string?> read, Action<FileChangeKind, string?> onChanged)
    {
        _read = read;
        _onChanged = onChanged;
        try { _diskText = read(); } catch { _diskText = null; }

        _debounce = new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (dir == null || name.Length == 0) return;

        try
        {
            _watcher = new FileSystemWatcher(dir)
            {
                Filter = name,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size |
                               NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => Schedule();
            _watcher.Created += (_, _) => Schedule();
            _watcher.Deleted += (_, _) => Schedule();
            _watcher.Renamed += (_, _) => Schedule();
            _watcher.Error += (_, _) => Schedule();
        }
        catch (Exception)
        {
            _watcher = null;
        }
    }

    public string? DiskText
    {
        get { lock (_gate) return _diskText; }
    }

    public void UpdateDiskText(string text)
    {
        lock (_gate) _diskText = text;
    }

    public void Refresh() => Schedule();

    private void Schedule()
    {
        if (_disposed) return;
        try { _debounce.Change(DebounceMs, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    private void Flush()
    {
        if (_disposed) return;

        string? text;
        try
        {
            text = _read();
            _retries = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (++_retries <= MaxReadRetries) Schedule();
            else _retries = 0;
            return;
        }
        catch (Exception)
        {
            return;
        }

        FileChangeKind kind;
        lock (_gate)
        {
            if (_disposed) return;
            if (text == null)
            {
                if (_diskText == null) return;
                _diskText = null;
                kind = FileChangeKind.Deleted;
            }
            else
            {
                if (text == _diskText) return;
                _diskText = text;
                kind = FileChangeKind.Modified;
            }
        }
        _onChanged(kind, text);
    }

    public void Dispose()
    {
        _disposed = true;
        _watcher?.Dispose();
        _watcher = null;
        _debounce.Dispose();
    }
}

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Zypora;

public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";
    public string? Url2 { get; init; }
    public string? Sha256 { get; init; }
    public string? Notes { get; init; }
}

public sealed class UpdateApplyResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    public static UpdateApplyResult Ok() => new() { Success = true };
    public static UpdateApplyResult Fail(string error) => new() { Success = false, Error = error };
}

// 版本检查 / 下载 / 校验 / 解压 / 自我替换
public static class UpdateService
{
    public const string ExeName = "Zypora.exe";
    public const string ZipName = "Zypora-win-x64.zip";

    private static readonly HttpClient Http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    // ---- 版本 ----

    public static string CurrentVersion() => CurrentVersion(typeof(UpdateService).Assembly);

    public static string CurrentVersion(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info)) return "";
        int plus = info.IndexOf('+');
        return plus >= 0 ? info.Substring(0, plus) : info;
    }

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        int plus = s.IndexOf('+');
        if (plus >= 0) s = s.Substring(0, plus);
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        if (s.Length == 0) return null;
        return Version.TryParse(s, out var v) ? v : null;
    }

    public static bool IsNewer(string? candidate, string? current)
    {
        var a = ParseVersion(candidate);
        var b = ParseVersion(current);
        if (a == null || b == null) return false;
        return a > b;
    }

    // ---- update.json ----

    public static UpdateInfo? ParseInfo(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string? Get(string name) =>
                root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

            var version = Get("version");
            var url = Get("url");
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url)) return null;

            var url2 = Get("url2");
            var sha = Get("sha256");
            return new UpdateInfo
            {
                Version = version!.Trim(),
                Url = url!.Trim(),
                Url2 = string.IsNullOrWhiteSpace(url2) ? null : url2!.Trim(),
                Sha256 = string.IsNullOrWhiteSpace(sha) ? null : sha!.Trim(),
                Notes = Get("notes"),
            };
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<string> CandidateUrls(string? primary = null)
    {
        var list = new List<string>();
        void Add(string? url)
        {
            if (!string.IsNullOrWhiteSpace(url)) list.Add(url.Trim());
        }

        Add(primary ?? UpdateConfig.PrimaryUpdateUrl);
        Add(UpdateConfig.JsDelivrUpdateUrl);
        Add(UpdateConfig.GitHubRawUpdateUrl);
        return list;
    }

    // ---- 检查(失败一律静默) ----

    public static async Task<UpdateInfo?> CheckAsync(string? primary = null, CancellationToken ct = default)
    {
        foreach (var url in CandidateUrls(primary))
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(UpdateConfig.CheckTimeout);
                using var resp = await Http.GetAsync(url, cts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                var info = ParseInfo(text);
                if (info != null) return info;
            }
            catch
            {
                // 静默:任何失败都尝试下一个源
            }
        }
        return null;
    }

    // ---- 下载 / 校验 / 解压 ----

    public static string TempDir(string version)
    {
        var safe = new string((version ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_').ToArray());
        if (safe.Length == 0) safe = "update";
        return Path.Combine(Path.GetTempPath(), "Zypora-update", safe);
    }

    public static async Task<bool> DownloadAsync(UpdateInfo info, string destFile, IProgress<double>? progress, CancellationToken ct = default)
    {
        var urls = new[] { info.Url, info.Url2 }.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u!.Trim()).ToList();
        foreach (var url in urls)
        {
            try
            {
                if (!await TryDownloadAsync(url, destFile, progress, ct).ConfigureAwait(false)) continue;
                if (string.IsNullOrWhiteSpace(info.Sha256) || VerifySha256(destFile, info.Sha256!)) return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 该源失败,试下一个
            }
            TryDelete(destFile);
        }
        return false;
    }

    private static async Task<bool> TryDownloadAsync(string url, string destFile, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        HttpResponseMessage resp;
        using (var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            headerCts.CancelAfter(UpdateConfig.DownloadHeaderTimeout);
            resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode) return false;

            using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bodyCts.CancelAfter(UpdateConfig.DownloadTimeout);

            var total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(bodyCts.Token).ConfigureAwait(false);
            await using var dst = File.Create(destFile);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, bodyCts.Token).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), bodyCts.Token).ConfigureAwait(false);
                read += n;
                if (total is > 0) progress?.Report(Math.Min(1.0, (double)read / total.Value));
            }

            return read > 0 && (total == null || read == total.Value);
        }
    }

    public static string Sha256OfFile(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    public static bool VerifySha256(string path, string expected)
    {
        try
        {
            return string.Equals(Sha256OfFile(path), expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool ExtractUpdate(string zipPath, string extractDir, out string? error)
    {
        error = null;
        try
        {
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            if (!File.Exists(Path.Combine(extractDir, ExeName)))
            {
                error = "更新包中缺少 " + ExeName;
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ---- 自我替换(Windows 允许重命名正在运行的 exe) ----

    public static UpdateApplyResult ApplyUpdate(string appDir, string extractDir, string exeName = ExeName)
    {
        var target = Path.Combine(appDir, exeName);
        var incoming = Path.Combine(extractDir, exeName);
        var staged = target + ".new";
        var backup = target + ".bak";

        if (!File.Exists(incoming)) return UpdateApplyResult.Fail("更新包中缺少 " + exeName);

        try
        {
            File.Copy(incoming, staged, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDelete(staged);
            return UpdateApplyResult.Fail("写入新程序失败:" + ex.Message);
        }

        bool renamedOld = false;
        try
        {
            if (File.Exists(backup))
            {
                try
                {
                    File.Delete(backup);
                }
                catch
                {
                    TryDelete(staged);
                    return UpdateApplyResult.Fail("旧备份 " + exeName + ".bak 正被占用,无法更新");
                }
            }

            if (File.Exists(target))
            {
                File.Move(target, backup);
                renamedOld = true;
            }

            File.Move(staged, target);
        }
        catch (Exception ex)
        {
            if (renamedOld)
            {
                try
                {
                    if (!File.Exists(target)) File.Move(backup, target);
                }
                catch
                {
                    // 回滚失败:保留 .bak 供手工恢复
                }
            }
            TryDelete(staged);
            return UpdateApplyResult.Fail("替换主程序失败:" + ex.Message);
        }

        // 其余文件(LatoFont、使用说明.md 等)尽力覆盖;失败不影响主程序更新
        try
        {
            foreach (var file in Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(extractDir, file);
                if (string.Equals(rel, exeName, StringComparison.OrdinalIgnoreCase)) continue;
                var dst = Path.Combine(appDir, rel);
                var dstDir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
                try
                {
                    File.Copy(file, dst, overwrite: true);
                }
                catch
                {
                    // 单个文件失败忽略
                }
            }
        }
        catch
        {
            // 忽略
        }

        return UpdateApplyResult.Ok();
    }

    /// <summary>启动时清理上次更新留下的 .bak / .new(被占用则忽略)。</summary>
    public static void CleanupBackup(string appDir, string exeName = ExeName)
    {
        TryDelete(Path.Combine(appDir, exeName + ".bak"));
        TryDelete(Path.Combine(appDir, exeName + ".new"));
    }

    public static void StartNewVersion(string appDir, string? filePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(appDir, ExeName),
                WorkingDirectory = appDir,
                UseShellExecute = false,
            };
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath)) psi.ArgumentList.Add(filePath);
            Process.Start(psi);
        }
        catch
        {
            // 启动失败:用户可手动打开
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 忽略
        }
    }
}

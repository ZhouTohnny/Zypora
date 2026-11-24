using System.IO;
using System.Text.Json;

namespace Zypora;

public sealed class AppSettings
{
    public bool DarkMode { get; set; }

    // 主题代码:"light" / "dark" / "eye";为空时回退到旧的 DarkMode 字段
    public string? Theme { get; set; }

    // 用户选择"以后再说"的版本号:该版本不再提示
    public string? SkippedVersion { get; set; }

    // 后台预下载完成后,退出时自动安装更新(默认开启)
    public bool AutoUpdate { get; set; } = true;

    public AppTheme ResolveTheme() =>
        string.IsNullOrWhiteSpace(Theme)
            ? (DarkMode ? AppTheme.Dark : AppTheme.Light)
            : AppThemeCodes.Parse(Theme);
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zypora");

    private static string FilePath(string dir) => Path.Combine(dir, "settings.json");

    public static AppSettings Load(string dir)
    {
        try
        {
            var path = FilePath(dir);
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(string dir, AppSettings settings)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath(dir), JsonSerializer.Serialize(settings, WriteOptions));
    }
}

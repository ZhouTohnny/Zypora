using System.IO;
using System.Text.Json;

namespace Zypora;

public sealed class AppSettings
{
    public string? BackgroundImagePath { get; set; }
    public double BackgroundOpacity { get; set; } = 0.3;
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

    // 把用户选的背景图复制到设置目录,返回副本路径
    public static string ImportBackground(string sourceFile, string dir)
    {
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(sourceFile);
        if (string.IsNullOrEmpty(ext)) ext = ".png";

        foreach (var old in Directory.GetFiles(dir, "background.*"))
        {
            try { File.Delete(old); } catch { }
        }

        var dest = Path.Combine(dir, "background" + ext);
        File.Copy(sourceFile, dest, overwrite: true);
        return dest;
    }
}

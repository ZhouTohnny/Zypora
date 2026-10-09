using System.IO;
using System.Text.Json;

namespace Zypora;

public sealed class AppSettings
{
    public bool DarkMode { get; set; }
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

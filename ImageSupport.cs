using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Zypora;

public static class ImageSupport
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp",
    };

    private static readonly Regex ImageLineRegex = new(@"^!\[(.*?)\]\((.+)\)$", RegexOptions.Compiled);

    public static bool IsImageExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        return Extensions.Contains(Path.GetExtension(path));
    }

    public static bool IsRemote(string src)
        => !string.IsNullOrEmpty(src) &&
           (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    // 整行(trim 后)形如 ![alt](src) 才成立
    public static bool TryParseImageLine(string line, out string alt, out string src)
    {
        alt = "";
        src = "";
        if (string.IsNullOrEmpty(line)) return false;

        var m = ImageLineRegex.Match(line.Trim());
        if (!m.Success) return false;

        alt = m.Groups[1].Value;
        src = m.Groups[2].Value.Trim();
        return src.Length > 0;
    }

    // 副本文件名:安全化原名 + "_" + token + 扩展名;无原名 → clip_<token>.png
    public static string MakeAssetFileName(string? originalName, string token)
    {
        var ext = ".png";
        var baseName = "";

        if (!string.IsNullOrEmpty(originalName))
        {
            var name = Path.GetFileName(originalName);
            var e = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(e))
            {
                ext = e;
                name = name.Substring(0, name.Length - e.Length);
            }
            baseName = Sanitize(name);
        }

        if (baseName.Length == 0) baseName = "clip";
        return $"{baseName}_{token}{ext}";
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (Array.IndexOf(invalid, ch) < 0) sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    // 解析可显示的本地路径;找不到或远程地址时原样返回
    public static string ResolveImagePath(string src, string? docDir, string? appDir)
    {
        if (string.IsNullOrWhiteSpace(src) || IsRemote(src)) return src;

        var local = src.Replace('/', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(local) && File.Exists(local)) return local;

        if (!string.IsNullOrEmpty(docDir))
        {
            var p = Path.Combine(docDir, local);
            if (File.Exists(p)) return p;
        }

        if (!string.IsNullOrEmpty(appDir))
        {
            var p = Path.Combine(appDir, local);
            if (File.Exists(p)) return p;

            var byName = Path.Combine(appDir, "assets", Path.GetFileName(local));
            if (File.Exists(byName)) return byName;
        }

        return src;
    }

    // 文档中下一个未使用的 ![image N] 序号
    public static int NextImageIndex(string source)
    {
        int max = 0;
        foreach (Match m in Regex.Matches(source, @"!\[image (\d+)\]"))
        {
            if (int.TryParse(m.Groups[1].Value, out var v) && v > max) max = v;
        }
        return max + 1;
    }

    // 在光标所在行行首插入图片 Markdown(每行一张),返回新文本与光标位置
    public static (string Text, int Caret) InsertImageLines(string source, int caret, IReadOnlyList<string> absolutePaths)
    {
        if (caret < 0) caret = 0;
        if (caret > source.Length) caret = source.Length;
        int lineStart = caret == 0 ? 0 : source.LastIndexOf('\n', caret - 1) + 1;

        string newline = source.Contains("\r\n") ? "\r\n" : (source.Contains("\n") ? "\n" : "\r\n");
        int n = NextImageIndex(source);
        var lines = absolutePaths
            .Select(p => $"![image {n++}]({p.Replace('\\', '/')})")
            .ToArray();
        var block = string.Join(newline, lines);

        var text = source.Insert(lineStart, block + newline);
        return (text, lineStart + block.Length);
    }

    // 复制图片文件到 assets,返回目标绝对路径
    public static string CopyFileToAssets(string sourceFile, string assetsDir, string token)
    {
        Directory.CreateDirectory(assetsDir);
        var name = MakeAssetFileName(Path.GetFileName(sourceFile), token);
        var dest = Path.Combine(assetsDir, name);
        File.Copy(sourceFile, dest, overwrite: false);
        return dest;
    }

    // 把位图编码为 PNG 存到 assets,返回目标绝对路径
    public static string SaveBitmapToAssets(BitmapSource image, string assetsDir, string token)
    {
        Directory.CreateDirectory(assetsDir);
        var name = MakeAssetFileName(null, token);
        var dest = Path.Combine(assetsDir, name);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var fs = File.Create(dest);
        encoder.Save(fs);
        return dest;
    }
}

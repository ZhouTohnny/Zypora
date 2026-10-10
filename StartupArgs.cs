using System.IO;

namespace Zypora;

// 处理命令行参数(右键"打开方式" / 拖到 exe / 命令行传入的文件)
public static class StartupArgs
{
    public const string NoUpdateCheckArg = "--no-update-check";

    public static IReadOnlyList<string> OpenableFiles(IEnumerable<string> args)
    {
        var result = new List<string>();
        foreach (var a in args)
        {
            if (!string.IsNullOrWhiteSpace(a) && File.Exists(a)) result.Add(a);
        }
        return result;
    }

    public static bool HasNoUpdateCheck(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, NoUpdateCheckArg, StringComparison.OrdinalIgnoreCase));
}

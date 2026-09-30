namespace Zypora;

public static class AppInfo
{
    // 把 InformationalVersion(可能含 "+<commit>" 构建信息)整理成标题用的 " v1.0.1"
    public static string VersionSuffix(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion)) return "";

        int plus = informationalVersion.IndexOf('+');
        if (plus >= 0) informationalVersion = informationalVersion.Substring(0, plus);

        return informationalVersion.Length == 0 ? "" : " v" + informationalVersion;
    }
}

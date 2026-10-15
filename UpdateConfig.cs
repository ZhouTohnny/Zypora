namespace Zypora;

// 更新源与超时配置
public static class UpdateConfig
{
    // 主更新源:填写 update.json 的完整地址(国内可访问的直链),例如:
    //   "https://example.com/zypora/update.json"
    // 留空则只用下面的备源。
    public const string PrimaryUpdateUrl = "";

    // 备源 1:jsDelivr 读取仓库中的 update.json(国内一般可访问,但有缓存延迟)
    public const string JsDelivrUpdateUrl = "https://cdn.jsdelivr.net/gh/ZhouTohnny/Zypora@main/update.json";

    // 备源 2:GitHub raw(国内通常不通,仅作兜底)
    public const string GitHubRawUpdateUrl = "https://raw.githubusercontent.com/ZhouTohnny/Zypora/main/update.json";

    // GitHub Releases API:取"最新 Release"(需带 Zypora-win-x64.zip 资产);
    // 有它就能自动跟随 GitHub 上发的 Release,不必每次改 update.json
    public const string ReleasesApiUrl = "https://api.github.com/repos/ZhouTohnny/Zypora/releases/latest";

    // 启动检查:每个源最多等待 5 秒
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    // 下载:连接/响应头最多 30 秒,整体最多 10 分钟
    public static readonly TimeSpan DownloadHeaderTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
}

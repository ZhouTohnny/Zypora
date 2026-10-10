# Zypora 更新机制设计(启动检查 + 一键自动更新)

日期:2026-10-10
状态:已与用户确认,待实现

## 1. 目标

- 程序启动后**后台检查**是否有新版本;检查失败(超时/连不上/数据异常)**完全静默**,不弹异常、不影响使用。
- 有新版时提示用户,用户可选「立即更新」或「以后再说」(记住跳过的版本)。
- 选择「立即更新」→ 程序内**下载 → 校验 → 解压 → 自我替换 → 重启**,全程无需手工操作。
- **下载失败要给出友好提示**(用户主动发起的操作,不能让人干等)。

## 2. 非目标(YAGNI)

- 不做增量/差分更新,每次下载完整 zip。
- 不做后台静默安装:必须用户点击「立即更新」。
- 不做多语言、不做更新频率限制(每次启动最多检查一次)。

## 3. 更新信息源

小文件 `update.json`(约 1KB),按顺序尝试,任一个成功即停止:

1. **主源(用户提供的国内直链)**:常量 `UpdateConfig.PrimaryUpdateUrl`(待用户填写)
2. **jsDelivr**:`https://cdn.jsdelivr.net/gh/ZhouTohnny/Zypora@main/update.json`
3. **GitHub raw**(兜底,国内基本不通,无害):`https://raw.githubusercontent.com/ZhouTohnny/Zypora/main/update.json`

`update.json` 格式:

```json
{
  "version": "1.0.3",
  "url":  "https://国内直链/zypora/Zypora-win-x64.zip",
  "url2": "https://github.com/ZhouTohnny/Zypora/releases/download/v1.0.3/Zypora-win-x64.zip",
  "sha256": "可选;填了则校验下载包",
  "notes": "可选;一句话更新说明"
}
```

- `version`、`url` 必填;`url2`、`sha256`、`notes` 可选。
- 仓库根目录维护一份 `update.json`(作为 jsDelivr/GitHub 备源)。

## 4. 检查流程

- 触发点:`App.OnStartup` 显示主窗口之后,`Task.Run` 后台执行;每进程只检查一次。
- 命令行参数 `--no-update-check` 可跳过检查(便于开发/测试)。
- 超时:**每个源 5 秒**;使用 `HttpClient`,总耗时不超过约 15 秒。
- 版本比较:当前版本取自 exe 的 `InformationalVersion`(去掉 `+hash`);`System.Version` 解析,新版本 > 当前版本才提示。
- **任何异常一律吞掉**(静默),不写弹窗。

## 5. 提示交互

新增 `UpdateWindow.xaml`(约 420×240,居中于主窗口,`Topmost`),两态:

**提示态**
- 标题:发现新版本
- 正文:`发现新版本 v1.0.3(当前 v1.0.2)`;有 `notes` 则另起一行显示
- 按钮:**立即更新** / **以后再说**

**进度态**(点「立即更新」后同窗口切换)
- 进度条 + `正在下载更新… 42%`(无 `Content-Length` 时显示「正在下载…」)
- 按钮:**取消**

「以后再说」→ 写入 `%APPDATA%\Zypora\settings.json` 的 `SkippedVersion`;之后不再为该版本提示(更高版本仍会提示)。

## 6. 更新流程

1. 下载 zip 到 `%TEMP%\Zypora-update\<version>\Zypora-win-x64.zip`
   - 先试 `url`,失败再试 `url2`
   - 超时:连接与响应头 30 秒,下载总时长上限 10 分钟;支持取消
   - 若提供 `sha256` 则校验,不匹配视为该源失败并尝试下一个
2. 解压到 `%TEMP%\Zypora-update\<version>\extract\`;要求根目录存在 `Zypora.exe`,否则视为失败
3. **更新前保护**:若当前文档有未保存改动,先走既有的保存确认(保存 / 不保存 / 取消更新)
4. **自我替换**(已验证 Windows 允许重命名正在运行的 exe):
   1. 新 exe 复制为 `Zypora.exe.new`
   2. `Zypora.exe` → `Zypora.exe.bak`(若 `.bak` 已存在则先删除;删不掉则中止并提示)
   3. `Zypora.exe.new` → `Zypora.exe`
   4. 覆盖复制 `LatoFont\`、`使用说明.md`(这些文件未被锁定;**不动 `assets\` 与用户文件**)
   5. 启动新 `Zypora.exe`(带当前打开的文件路径,若有)
   6. 退出当前进程
5. **回滚**:第 4 步任一子步骤失败 → 尽力把 `.bak` 改回 `Zypora.exe`,删除 `Zypora.exe.new`,提示「更新失败」,程序继续可用。
6. **启动清理**:每次启动时若发现 `Zypora.exe.bak`,尝试删除(失败则忽略,说明有另一个实例在运行)。

## 7. 失败提示边界

| 场景 | 行为 |
| --- | --- |
| 启动检查失败(超时/连不上/JSON 损坏) | **完全静默** |
| 用户点「立即更新」后下载失败(所有源) | 提示「更新失败:无法下载更新包,请稍后重试」 |
| 校验失败 / 解压失败 / 目录不可写 | 提示「更新失败:<简短原因>」,程序继续正常运行 |
| 用户点「取消」 | 中止下载、清理临时文件,不打扰 |

## 8. 代码结构

| 文件 | 职责 |
| --- | --- |
| `UpdateConfig.cs` | `PrimaryUpdateUrl` 常量、jsDelivr/GitHub 备源地址、超时等常量 |
| `UpdateService.cs` | 版本比较、`update.json` 解析、候选 URL 顺序、下载(含进度/取消)、SHA-256 校验、解压、自我替换与回滚。网络与文件操作拆成可注入的小方法,便于单测 |
| `UpdateWindow.xaml(.cs)` | 提示态 + 进度态二合一窗口,只负责 UI,调用 `UpdateService` |
| `AppSettings.cs` | 新增 `SkippedVersion`(`string?`),随 `settings.json` 持久化 |
| `App.xaml.cs` | 触发一次后台检查;启动时清理 `.bak` |
| `StartupArgs.cs` | 识别 `--no-update-check` |

关键接口(便于测试):

```csharp
bool UpdateService.IsNewer(string candidate, string current);
UpdateInfo? UpdateService.ParseInfo(string json);              // version/url 缺失或 JSON 损坏 → null
IReadOnlyList<string> UpdateService.CandidateUrls(string primary);
ApplyResult UpdateService.ApplyUpdate(string appDir, string extractDir, ...); // 可注入路径,便于在临时目录测试
```

## 9. 测试计划(加入 tests/Zypora.Tests)

- 版本比较:`1.0.3 > 1.0.2`、`1.0.2 == 1.0.2`、`1.0.2+abc` 视为 `1.0.2`、`1.0.10 > 1.0.9`、`2.0 > 1.9.9`、非法串 → 不提示
- `update.json` 解析:合法 / 缺 `version` / 缺 `url` / 空串 / 损坏 JSON / `notes` 可选
- 候选 URL 顺序:主源在前,备源在后,空值过滤
- 跳过版本:与当前比较、与 SkippedVersion 比较
- **自我替换**(在临时目录内做,全部用假 exe 文本文件):
  - 正常:新 exe 就位、`.bak` 生成、其他文件被覆盖、用户文件保留
  - 失败回滚:目标 exe 不可改名时保持原样并返回失败
- `--no-update-check` 参数解析

## 10. 开发者发布新版本流程

1. 修改 `Zypora.csproj` 的 `<Version>`
2. `dotnet publish ...` 打包并压成 `dist\Zypora-win-x64.zip`
3. 上传 zip 到国内直链目录
4. 更新仓库 `update.json` 的 `version` / `url` / `sha256` / `notes`(jsDelivr 有缓存,主源才是实时的)
5. (可选)发 GitHub Release,填 `url2`

## 11. 风险与限制

- 国内网络:主源可用性由用户保证;jsDelivr 有缓存(可能延迟数小时);GitHub raw 基本不通。
- 若程序放在无写权限目录(如 `Program Files`),更新会失败并提示;当前分发方式为自解压 zip,默认有写权限。
- 杀毒软件可能拦截 exe 替换/自启动 → 失败时提示用户手动下载。
- 更新过程中断电/强杀 → `Zypora.exe.bak` 保留,可手工改回 `Zypora.exe`。
- 多开窗口(`Ctrl+N`)时只有发起更新的那个实例会重启,其他实例仍运行旧版本,需用户自行关闭。

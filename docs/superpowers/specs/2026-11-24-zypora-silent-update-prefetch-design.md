# 静默预下载与自动更新 设计

日期:2026-11-24
状态:已确认

## 目标

1. 程序启动后在后台静默检查并下载新版本包,用户无需等待下载。
2. 手动「检查更新」时,如果包已下载完成,点「立即更新」只做解压与替换,几秒内完成并重启。
3. 设置中增加「更新管理 → 自动更新」开关(默认开启):
   - 开启:后台下载完成后不打扰用户,正常退出时静默安装,下次启动即为新版。
   - 关闭:只预下载,不自动安装;工具栏提示「新版本 vX 已就绪」,由用户手动安装。
4. 启动时不再弹更新窗口(完全静默),避免打断。
5. 不破坏现有行为:手动检查不受「跳过此版本」限制;未保存提示、更新完成后的变更说明、SHA 校验、失败静默都保留。

## 行为规则

### 启动预下载

- 启动后(未传 `--no-update-check`)静默检查版本;失败不提示。
- 新版本且不是「跳过此版本」时:若临时目录已有同版本且 SHA 校验通过的包,直接视为就绪;否则后台下载到
  `%TEMP%\Zypora-update\<version>\Zypora-win-x64.zip` 并校验 SHA。
- 下载单飞:同一进程内只会有一个下载任务。
- 就绪后:
  - 自动更新开启:不显示任何提示;退出时安装。
  - 自动更新关闭:当前所有窗口工具栏显示常驻提示「新版本 vX 已就绪,可在 设置 → 更新管理 安装」。

### 手动检查更新

- 「设置 → 更新管理 → 检查更新…」仍先联网查版本(元数据请求,很快)。
- 发现新版本时打开更新窗:
  - 包已就绪:窗口显示「更新包已就绪」,点「立即更新」跳过下载,直接解压替换并重启。
  - 包正在下载:窗口接上同一个下载任务并显示进度,取消只关闭窗口,不中断后台预下载。
  - 未开始下载:由更新窗发起下载(与启动预下载共用同一套逻辑)。

### 自动更新(设置项)

- `settings.json` 新增 `autoUpdate`(默认 `true`;旧配置缺该字段视为开启)。
- 齿轮菜单:`版本 vX.Y.Z` / `更新管理 ▸`(`自动更新` 勾选项、`检查更新…`)。
- 切换开关立即保存。关闭后不再在退出时安装;已就绪的包保留到临时目录,手动检查时可直接用。
- 开启且退出时包已就绪:
  - 在 `App.OnExit` 中解压并以现有 `ApplyUpdate` 替换主程序与附属文件,写入 pending 更新说明;失败静默(下次启动仍是旧版,可重试)。
  - 不在退出时启动新进程,下次用户正常打开即新版。
  - 若本次退出是由更新窗「立即更新」触发的重启,则跳过退出安装,避免重复替换。

### 边界

- 多窗口共用同一进程级 `UpdateCenter`;提示显示在所有窗口。
- 下载/校验失败:清空任务,不提示;下次启动或手动检查会重试。
- 退出安装失败:保留 `.bak/.new` 现状,启动时既有 `CleanupBackup` 处理。

## 组件

- `UpdateCenter`(新增,进程级,可注入依赖以便单测):
  - `StartBackground(currentVersion, AppSettings)`:幂等启动静默检查+预下载;供 `App` 启动时调用。
  - `IsReady` / `ReadyInfo` / `ReadyZip`。
  - `EnsureReadyAsync(UpdateInfo, IProgress<double>?, CancellationToken)`:已就绪直接成功;下载中则复用同一任务并转发进度;未开始则启动下载。
  - `AddProgress(IProgress<double>)` → 返回取消订阅委托。
  - `ApplyReadyAtExit(string appDir, string settingsDir)`:解压 + `ApplyUpdate` + `WritePendingInfo`;返回是否成功。
  - `MarkInstalled()`:供更新窗立即安装后调用,避免退出时重复安装。
  - 依赖注入:版本检查、下载、临时目录、解压/替换函数,默认走 `UpdateService`。
- `AppSettings`:新增 `AutoUpdate`(默认 true)。
- `App`:
  - 移除启动时弹更新窗的逻辑,改为 `UpdateCenter.Shared.StartBackground(...)`。
  - `OnExit` 中按设置执行 `ApplyReadyAtExit`。
- `MainWindow`:
  - 齿轮菜单改为「更新管理」子菜单(自动更新 / 检查更新…),打开菜单时同步勾选状态。
  - 手动检查发现新版本时把 `UpdateCenter.Shared` 传给更新窗。
  - 自动更新关闭且包就绪时显示工具栏提示。
- `UpdateWindow`:
  - 构造增加可选 `UpdateCenter`。
  - 「立即更新」优先走 `EnsureReadyAsync`(已就绪零下载),再解压替换;成功后 `MarkInstalled` 并重启。
- `UpdateService`:不改协议;复用现有 `TempDir`/`DownloadAsync`/`VerifySha256`/`ExtractUpdate`/`ApplyUpdate`/`WritePendingInfo`。

## 测试

- `AppSettings.AutoUpdate`:缺省为 true;旧 JSON(无该字段)读出 true;写入 false 后往返仍为 false。
- `UpdateCenter`(注入假检查/假下载/临时目录):
  - 新版本 → 下载一次 → 就绪;同版本/旧版本 → 不下载。
  - 「跳过此版本」→ 不下载。
  - 临时目录已有包 → 直接就绪,不下载。
  - 并发 `EnsureReadyAsync` 只触发一次下载;两个进度订阅都收到 100%。
  - 下载失败 → 不就绪;再次调用会重试。
  - `ApplyReadyAtExit`:用构造的临时 zip 与假 app 目录验证替换出 `Zypora.exe` 并写入 pending 说明(失败场景不抛异常)。
- `UpdateWindow`:已就绪时不再调用下载(通过注入假的 center 验证);该测试不联网。
- 菜单:齿轮菜单存在「更新管理」与可勾选的「自动更新」。
- 既有 418 项测试保持通过;Debug/Release 0 警告 0 错误。

## 不在本次范围

- 定时轮询(仅在启动与手动检查时查版本)。
- 后台静默下载的限速/代理设置。
- 自动重启安装(已确认仅退出时安装)。

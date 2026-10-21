# 齿轮设置菜单 + 手动检查更新 — 设计

日期:2026-10-21
状态:已确认

## 目标

1. 工具栏增加齿轮「设置」入口,内容少但目前只放版本检查,后续可扩展。
2. 支持手动发起更新检查,结果可见(不再静默)。
3. 修复更新源选择缺陷:jsDelivr 缓存旧版会挡住 GitHub raw 的新版,导致明明有新版本却不提示。

## 工具栏

- 位置:置顶按钮之后(工具栏最右)。
- 外观:16px 齿轮图标(填充随主题前景色),ToolTip「设置」,点击弹出下拉菜单(沿用现有主题化 ContextMenu/MenuItem 样式)。
- 菜单内容:
  - `版本 vX.Y.Z`:禁用态(半透明),显示当前版本
  - 分隔线
  - `检查更新…`:手动检查
  - 后续设置项直接往菜单里加

## 手动检查流程

- 进行中:齿轮左侧显示状态文字「正在检查更新…」,检查期间忽略重复点击。
- 已是最新:`已是最新版本 vX.Y.Z`,4 秒后收起。
- 发现新版本:`发现新版本 vX.Y.Z`,并打开现有 `UpdateWindow`(更新内容 / 下载安装 / 跳过)。
- 失败:`检查更新失败:<原因>`,6 秒后收起。
- 手动检查**忽略** `SkippedVersion`;用户仍可在更新窗里选择跳过(选择后照旧写入跳过版本)。

## 更新源选择(修复)

- `CheckJsonAsync`:所有候选源(自定义直链 / jsDelivr / GitHub raw)**并行**请求,每个 5 秒超时,取**版本最高**的有效结果。
- 新增纯函数 `UpdateService.PickBest(IEnumerable<UpdateInfo?>)`:按 `IsNewer` 选最高版本,null 忽略。
- `CheckAsync` 与新增的 `CheckManualAsync` 都用 `PickBest` 合并 JSON 源与 GitHub Release 源。
- `CheckManualAsync` 返回结果对象(是否失败 + 摘要),供 UI 展示;自动检查仍静默。

## 测试

- `PickBest`:单源、新旧混合、含 null、全部 null。
- 现有 `IsNewer` / `ParseInfo` / `ParseRelease` 回归。
- 探针只读:齿轮按钮外观、菜单打开截图、版本项文字、状态文字样式。

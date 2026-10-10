# Zypora 主题三档(常规/夜间/护眼)+ 沉浸式阅读模式 设计

日期:2026-10-10
状态:已与用户确认,待实现

## 1. 目标

1. **主题下拉**:工具栏去掉"灯泡"开关,改为 `主题 ▾` 下拉,含 **常规 / 夜间 / 护眼** 三档(护眼 = 豆沙绿)。
2. **下拉菜单跟随主题**:切换主题后,工具栏下拉菜单(文件 / 格式 / 主题)的背景、文字、悬停、边框与分隔线同步变色,不再突兀。
3. **阅读模式**:在 预览 / 原生 之外新增沉浸式阅读视图 —— 隐藏工具栏、查找栏、窗口标题栏与边框,只读、纯预览排版;右上角保留半透明「退出阅读」按钮,`Esc` 亦可退出,退出后回到预览可继续编辑。

## 2. 非目标(YAGNI)

- 不做自定义主题 / 色板编辑。
- 阅读模式不做窗口拖动、最小化(用户已确认接受此限制)。
- 阅读模式不持久化(重启回到默认预览)。
- 菜单不含子菜单(现有三个菜单都是平铺;以后需要再加)。

## 3. 主题模型

- 新增 `public enum AppTheme { Light, Dark, Eye }`。
- `AppSettings` 新增 `string? Theme`(`"light"` / `"dark"` / `"eye"`)。
- 兼容旧配置:`Theme` 缺失时,`DarkMode == true` → Dark,否则 Light。
- 保存时同时写 `Theme` 与 `DarkMode = (Theme == Dark)`,旧版本读同一份 `settings.json` 不炸。

## 4. 配色

### 4.1 界面(ChromeTheme,纯数据,便于单测)

```csharp
public sealed record ChromeTheme(
    Color WindowBg, Color PanelBg, Color FindBg, Color PanelBorder,
    Color Fg, Color Muted, Color Hover, Color Checked, Color CheckedFg,
    Color EditorBg, Color Caret,
    Color MenuBg, Color MenuFg, Color MenuHover, Color MenuBorder);

public static ChromeTheme Of(AppTheme theme);
```

- Light / Dark **沿用现有色值**(窗口 #F4F4F4 / #1E1E1E,面板白 / #252526,编辑区白 / #1E1E1E 等)。
- Eye(护眼/豆沙绿):

| 键 | 值 |
| --- | --- |
| WindowBg / EditorBg | `#C7EDCC` |
| PanelBg | `#D6F0D9` |
| FindBg | `#CDEBD2` |
| PanelBorder | `#A8CBAE` |
| Fg | `#2E3B2E` |
| Muted | `#5C6E5C` |
| Hover | `#BCE3C2` |
| Checked / CheckedFg | `#A9D9B2` / `#1F5B33` |
| Caret | `#2E3B2E` |
| MenuBg / MenuFg / MenuHover / MenuBorder | `#D6F0D9` / `#2E3B2E` / `#BCE3C2` / `#A8CBAE` |

### 4.2 文档渲染(RenderTheme.Eye)

Text `#2E3B2E`、HeadingFg `#243024`、QuoteFg `#4A5A4A`、QuoteLine `#4E8C5A`、RuleLine `#A8CBAE`、CodeBg `#BCE3C2`、CodeFg `#A03030`、Faint `#8FAE95`、GridLine `#A8CBAE`、GridLineStrong `#7FA988`、PlaceholderBg `#C2E5C8`、Bullet `#2E3B2E`。

## 5. 工具栏与菜单

- 删除 `DarkButton`(灯泡 ToggleButton),新增 `ThemeMenuButton`(文本 `主题 ▾`,样式同 `格式 ▾`)。
- 菜单项:常规 / 夜间 / 护眼,`Tag` = `light` / `dark` / `eye`;`Click="OnSelectTheme"`。
- 当前主题项:Header 前缀 `✓ ` 且加粗;其余项前缀两个空格保持对齐。
- 位置:`清空` 之后、`置顶` 之前。
- 菜单主题化:窗口资源新增刷子 `Br.MenuBg / Br.MenuFg / Br.MenuHover / Br.MenuBorder`,并为 `ContextMenu`、`MenuItem`、`Separator` 提供隐式样式(自定义 MenuItem 模板:边框 + 内容,`IsHighlighted` 触发悬停色,`IsEnabled=False` 半透明)。
- `ApplyTheme()` 统一按 `ChromeTheme.Of(_theme)` 重建全部 `Br.*` 资源(含菜单 4 项)与编辑区背景/光标色。

## 6. 阅读模式

### 6.1 进入(`EnterReadingMode`)

1. 记录:进入前的 `WindowStyle`、`WindowState`、`RestoreBounds`(Normal 时)。
2. `WindowStyle = None`;`WindowState = Maximized`。
3. 隐藏工具栏 Border(命名为 `ToolbarBorder`)与查找栏。
4. 视图切到 Preview 渲染(若当前是原生)。
5. `Editor.IsReadOnly = true`。
6. 显示右上角 `ExitReadingButton`(Grid 叠加在编辑区之上,右侧 16、顶部 12;默认 `Opacity = 0.35`,鼠标悬停 `1.0`;文本「退出阅读」)。

### 6.2 退出(`ExitReadingMode`)

1. 恢复 `WindowStyle` / `WindowState`(Normal 时恢复原尺寸位置)。
2. 显示工具栏;隐藏退出按钮。
3. `Editor.IsReadOnly = false`。
4. `_mode = Preview` 并重新渲染;更新按钮文字。

### 6.3 快捷键与边角

- `Esc`:阅读模式下退出(窗口级 `PreviewKeyDown`,其他模式下不拦截)。
- 阅读模式下不提供查找/保存入口(工具栏隐藏,编辑器只读,查找栏保持隐藏)。
- 每个窗口独立阅读状态;不写入 settings。

## 7. 测试计划(加入 tests/Zypora.Tests)

- `ChromeTheme.Of`:三套配色非空、互不相同;Light/Dark 关键色与现有实现一致(窗口/面板/编辑区/光标)。
- `AppTheme` 解析:`"eye" → Eye`、`"dark" → Dark`、未知/空/大小写 → Light;`ToCode` 往返。
- 设置迁移:老 JSON `{"DarkMode":true}` → Dark;`{"DarkMode":false}` → Light;`{"Theme":"eye"}` → Eye;保存后 `DarkMode` 与 `Theme` 一致;未知 Theme 串回退 Light。
- `RenderTheme.Eye`:与 Light/Dark 不同;正文色 ≠ 背景色;护眼主题下 `ReadSource` 恒等。
- 菜单项 `Tag` → 主题映射(纯函数)。
- 阅读模式进入/退出的**可测部分**:模式恢复为 Preview、`IsReadOnly` 开关逻辑(抽成可注入的小函数或直接对 MainWindow 成员做只读断言较难时,用只读 Win32 检查窗口样式作为集成验证)。
- 出图:三套主题的菜单外观离屏渲染 PNG 目视确认。

## 8. 影响面

`RenderTheme.cs`、新增 `ChromeTheme.cs`、`AppSettings.cs`、`MainWindow.xaml(.cs)`、`tests/Zypora.Tests/Program.cs`、`README.md`、`使用说明.md`。

**不动 `D:\SoftWare\Zypora`**;只改源码并本地提交(不推送)。

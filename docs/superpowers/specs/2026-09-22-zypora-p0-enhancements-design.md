# Zypora P0 增强设计(撤销粒度 / 查找替换 / 未保存提示 / 表格)

- 日期:2026-09-22
- 状态:已与用户确认设计,待评审
- 范围:Zypora WPF Markdown 编辑器(`E:\MyProgram\Zypora`)P0 四项功能

## 背景与不变量

Zypora 的核心理念:**文档文本必须与 Markdown 源码逐字一致**。`EditableRenderer.ReadSource(FlowDocument)`
通过 `TextRange` 读回文本,必须等于用户所写的源码,以保证:光标映射、格式命令、保存不损坏源码。
任何渲染方案都不得破坏此不变量(表格方案围绕它专门设计,见第 4 节)。

现有相关实现:
- 视图两态 `ViewMode { Preview, Raw }`,`_mode` 实例字段,预览态可直接编辑并实时重渲染。
- 渲染调度:预览 `QueueRender()`(`Dispatcher.BeginInvoke` + `_rendering`/`_renderQueued` 重入保护);
  原生 `ScheduleRender()`(350ms 防抖)。
- 光标映射:`DocumentCaret.GetCaretOffset` / `GetPointerAtCharOffset`(字符级)。
- 撤销:`UndoHistory` 保存 `(Text, Caret)` 快照,上限 200;当前在 `ReRender` 中每次重渲染都 `Push`。

## 目标 / 非目标

**目标**
1. 撤销按"连续输入合并、命令各一步"分组。
2. 完整查找/替换(大小写、全词、正则;替换当前/全部)。
3. 脏标记 + 未保存提示(关闭/新建/清空/打开)。
4. 表格:预览渲染为真实表格外观,原生模式可编辑,保持文本恒等。

**非目标(YAGNI)**
- 预览态表格单元格内逐格编辑(需切换原生模式)。
- 表格列宽拖拽、单元格合并、富文本单元格。
- 查找跨文档/在文件中查找。
- 撤销栈持久化。

## 1. 撤销分组:连续输入合并 + 命令各一步

### 行为
- 连续打字/删除:**合并为一步**,以下任一情况断开:
  - 距上次改动停顿 > **800ms**;
  - 用户按下 **空格** 或 **回车**(词/行边界);
  - 发生一次**格式命令**(加粗/斜体/行内代码/代码块/标题/列表/引用/列表回车)。
- 每个格式命令**自成一步**。

### 实现(`MainWindow.xaml.cs`)
- 新增字段:`bool _pending`、`DispatcherTimer _groupTimer`(800ms,一次 Tick 后停)。
- `MarkChange()`:置 `_pending = true` 并重启 `_groupTimer`。
- `CommitPending()`:若 `_pending`,`_history.Push(GetSourceText(), caret)`,`_pending = false`,停计时器。
- `PushNow()`:立即 `_history.Push(...)` 并 `_pending = false`(命令用)。
- `_groupTimer.Tick` → `CommitPending()`。
- `ReRender()`:删除其中的 `_history.Push(...)`,改为在恢复光标后调用 `MarkChange()`。
- 命令路径(`ToggleInline` / `ToggleLinePrefix` / `ToggleHeading` / `OnCodeBlock` / `TryContinueList`):
  统一改为 **先 `CommitPending()` → 应用编辑 → `PushNow()`**。抽取一个 `RunCommand(Func<...>)`
  包装以免重复。
- `OnPreviewKeyDown`:在空格/回车分支打标记,使该次改动成为组边界(在键被处理并触发 `OnTextChanged`
  之后 `CommitPending()`)。
- `OnUndo`:先 `CommitPending()` 再 `_history.Undo()`;`OnRedo`:先 `CommitPending()` 再 `Redo()`。
- `Restore(...)` / `ClearDocument` / `OnOpen`:停 `_groupTimer`,`_pending = false`。
- `UndoHistory` 保持不变(去重与 200 上限已有);仅确保 `Push` 在文本相同但光标不同时更新光标。

### 验收
- 连续输入一段文字后 `Ctrl+Z` 一次撤销整段。
- 输入后立刻 `Ctrl+Z`:回到该段之前;`Ctrl+Y` 能恢复该段。
- `Ctrl+B` 加粗后 `Ctrl+Z` 仅撤销加粗,不撤销之前的打字。

## 2. 查找 / 替换(完整版)

### 新增 `SearchService.cs`(纯逻辑,可单测)
```
public sealed record SearchOptions(bool CaseSensitive, bool WholeWord, bool UseRegex);
public static IReadOnlyList<(int Start, int Length)> Find(string source, string query, SearchOptions o);
public static (string Text, int NewCaret) ReplaceOne(string source, (int Start,int Length) match, string replacement);
public static (string Text, int Count) ReplaceAll(string source, string query, string replacement, SearchOptions o);
```
- 非正则:转义后 `Regex` 匹配;全词 = `\b...\b`(对 `\w` 边界);大小写由 `RegexOptions` 控制。
- 正则:直接使用;每次 `Find` 使用 `RegexOptions.Multiline`;空匹配(零长)跳过以避免死循环。
- 结果按 `Start` 升序,不重叠。

### UI(`MainWindow.xaml`)
- 在工具栏下方新增 `Border x:Name="FindBar" Visibility="Collapsed"`(DockPanel.Dock=Top):
  - `TextBox FindBox`、`TextBox ReplaceBox`、`CheckBox CaseBox("Aa")`、`WholeWordBox("全词")`、`RegexBox(".*")`
  - 按钮:上一个 / 下一个 / 替换 / 全部替换 / 关闭
  - `TextBlock MatchLabel`(`n/m` 或 `无匹配`)
  - 查找模式隐藏替换框与"替换/全部替换";替换模式显示。

### 交互
- `Ctrl+F` → 显示 FindBar(查找模式),聚焦 `FindBox`。
- `Ctrl+H` → 显示 FindBar(替换模式),聚焦 `FindBox`。
- `Esc`(FindBar 内)→ 隐藏并回焦 `Editor`。
- 文本/选项变化 → 重新计算命中集 `_matches`,索引归零,高亮全部 + 选中当前 + `BringIntoView`。
- 下一个/上一个:循环移动当前索引。
- 替换:替换当前命中 → 重建文档 → 重算命中 → 选中下一个。
- 全部替换:一次替换所有命中并显示数量。
- 回车在 `FindBox` = 下一个;在 `ReplaceBox` = 替换。

### 高亮
- 用 `TextRange.ApplyPropertyValue(TextElement.BackgroundProperty, brush)` 上色:
  全部命中浅黄(`#FFF3A0`),当前命中橙(`#FFB347`)。
- 因重渲染会重建文档,`ReRender()` 末尾若 `FindBar` 可见则调用 `ApplyHighlights()` 重放。
- 命中偏移即源码偏移(恒等保证),经 `DocumentCaret.GetPointerAtCharOffset` 映射。

### 验收
- 三个选项各自生效;正则非法时不崩溃(显示无匹配)。
- 替换全部后文本正确、撤销可回退(替换视为命令,自成一步)。
- 重渲染(打字)后高亮仍在。

## 3. 脏标记 + 未保存提示

### 行为
- 用户改动 → `_dirty = true`;保存成功 / 打开 / 清空 → `_dirty = false`。
- 重渲染、切换视图模式**不**置脏(未改变文本)。
- 标题:`"{名称}{*?} - Zypora ({预览|原生})"`;`名称` = 文件名(无路径)或 `未命名`。
- `ConfirmDiscard()`:`!_dirty` 直接 `true`;否则 `MessageBox YesNoCancel`:
  - 保存 → 调 `Save()`;保存成功返回 `true`,取消/失败返回 `false`;
  - 不保存 → `true`;取消 → `false`。
- 拦截点:窗口关闭(`OnClosing` override,`e.Cancel = !ConfirmDiscard()`)、新建窗口、清空、打开。
- `OnSave` 重构为 `bool Save()`(保留"已保存"提示),供工具栏与 `ConfirmDiscard` 复用。

### 验收
- 改动后标题出现 `*`;保存后消失。
- 有未保存改动时关闭/新建/清空/打开会弹窗;选"取消"则操作中止。

## 4. 表格:真表格渲染 + 原生编辑(保持恒等)

### 关键约束(实测)
- WPF `Table` 的 `TextRange.Text` 用 **`\t` 分隔单元格、`\r\n` 分隔行**,固定不可改 → 真 `Table` **无法**逐字恒等。
- `InlineUIContainer` 在 `TextRange.Text` 中**恰好占 1 个空格**(已在列表圆点中验证)。
- 实测:把一行源码按**最后一个空格**拆成 `[隐藏 Run(前段)] + [InlineUIContainer] + [隐藏 Run(后段)]`,
  文本等于原行 + `\r\n`,**逐字恒等成立**。
- 边界:**行内没有空格时该技巧失效**(如 `|---|---|`)。

### 新增 `TableParser.cs`(纯逻辑,可单测)
```
public sealed record TableCell(string Text, int SourceStart, int SourceLength);
public sealed record TableRow(IReadOnlyList<TableCell> Cells, string RawLine);
public enum ColumnAlign { None, Left, Center, Right }
public sealed record TableModel(IReadOnlyList<TableRow> Rows, int ColumnCount,
                                IReadOnlyList<ColumnAlign> Align, bool HasHeader);
public static bool TryParse(IReadOnlyList<string> lines, int start, out TableModel model, out int end);
```
- 判定:第 `start` 行 `Trim()` 后含 `|`;第 `start+1` 行匹配分隔行
  `^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-+:?\s*)*\|?\s*$`;随后连续含 `|` 的行归入表格。
- 解析单元格:按未转义 `|` 切分,去掉首尾空单元格(前后导管道产生),`Trim()` 单元格文本。
- 对齐:由分隔行的 `:---` / `:--:` / `---:` 得出;列数取各行最大值(缺列补空)。

### 渲染(`EditableRenderer`,预览)
- `Build` 主循环在逐行前**先扫描表格块**:命中则整块交给 `AppendTable(doc, model, lines, preview)`,
  跳过对应行;其余逻辑不变。
- `AppendTable` 逐源码行产出一个 `Paragraph`:
  - **含空格的表格行**:`[隐藏 Run(line[0..lastSpace])] + [InlineUIContainer(Grid 行)] + [隐藏 Run(line[lastSpace+1..])]`。
    - Grid 行:每列一个 `Border`(细灰格线 + 内边距),内含 `TextBlock`(单元格文本,复用行内解析)。
    - 表头行:文本加粗 + 该行 Grid 底部边框加粗(充当分隔线)。
    - 列宽:固定像素 = `max(40, 该列最大字符数 * 9 + 16)`(`Border` 内边距已计入),保证列对齐;
      文本超宽时该列单元格换行。
  - **分隔行**:整行放入**隐藏 Run**(`FontSize=1`、透明),不叠加容器;不产生可视件。
  - **无空格数据行**:回退为普通段落(管道符淡化、可见可编辑),保证恒等且不丢内容。
- 抽取 `AppendInlineMarkdown` 的行内解析为可复用的 `IEnumerable<Inline> ParseInlines(string, bool)`,
  供段落与 `TextBlock` 共用(单元格内标记在 UI 元素中,不影响 `TextRange`)。
- 原生模式(`BuildRaw`)不变:表格行即普通文本,可自由编辑。

### 限制(写入 `使用说明.md`)
- 预览态表格**不可逐格编辑**,需切换到"原生"编辑表格。
- 完全无空格的紧凑表格降级为文本显示。

### 验收(含恒等 fuzz)
- 对含表格的多种源码,`ReadSource(BuildPreview(src)) == src`(逐字)。
- 预览表格显示为带边框的网格、表头加粗、按对齐列排布;切换原生后为可编辑文本。
- 光标在表格上下移动、在表格前后输入不损坏文本。

## 新增 / 改动文件

| 文件 | 变更 |
|---|---|
| `SearchService.cs` | 新增(查找/替换纯逻辑) |
| `TableParser.cs` | 新增(表格解析纯逻辑) |
| `MainWindow.xaml` | 新增 `FindBar` |
| `MainWindow.xaml.cs` | 撤销分组、查找交互、脏标记与提示、快捷键 |
| `EditableRenderer.cs` | 表格渲染;`ParseInlines` 抽取 |
| `UndoHistory.cs` | 无需改动(保持不变) |
| `使用说明.md` | 表格编辑限制、查找/撤销说明 |
| `README.md` | 功能与快捷键更新 |

## 测试计划(临时测试工程 `Zypora.Tests`,追加)

- `SearchService`:大小写、全词、正则、零长匹配、替换当前、全部替换计数。
- `TableParser`:识别/边界、单元格切分、对齐、列数补齐、无空格行回退。
- **恒等 fuzz**:随机组合含表格/列表/标题/代码块的源码,`ReadSource(BuildPreview(src)) == src`;
  同时覆盖 `BuildRaw`。
- `UndoHistory`:去重、光标更新、200 上限、undo/redo 边界。
- 现有 185 项测试保持通过。

## 风险与缓解

- **表格恒等依赖"最后一个空格"技巧** → 无空格行已设计回退;新增 fuzz 保证。
- **预览态点入表格落在隐藏文本** → 已知限制,文档说明;不阻塞 P0。
- **撤销分组与重渲染时序** → 通过 `_pending`/`CommitPending` 单一入口,`Restore`/打开/清空显式复位。
- **查找高亮被重渲染清空** → `ReRender` 末尾重放。

## 验收总览(Definition of Done)

1. 四项功能按上文行为工作。
2. `ReadSource(BuildPreview(src)) == src` 对含表格源码成立(fuzz)。
3. 新旧测试全部通过;构建 0 警告 0 错误。
4. `使用说明.md` / `README.md` 更新。

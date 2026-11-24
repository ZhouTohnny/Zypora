# 预览表格右键复制 设计

日期:2026-11-12
状态:已确认

## 背景

预览模式下表格的可见文字由 `InlineUIContainer` 里的 `TextBlock` 绘制,不属于文档文本;鼠标拖选会被视觉元素挡住,键盘复制得到的是带 `|` 的 Markdown 源码而不是单元格内容。

## 行为规则

- **左键点选 / 拖选 + `Ctrl+C`**:
  - 在单元格上左键点击可高亮选中该单元格,按住左键跨格拖动可框选一片单元格区域;
  - 按 `Ctrl+C` 直接复制所选单元格或区域(单格为纯文本,多格为制表符/换行分隔的 TSV),并在工具栏显示「已复制单元格 / 已复制选中表格内容」胶囊提示。
- **右键菜单**:
  - 在任意单元格上右键(由 `Editor` 通过 `VisualTreeHelper.HitTest` 命中 `InlineUIContainer` 内部单元格并弹出主题化菜单):
    - 「复制单元格」:复制该单元格的可见文本(去掉首尾空白及 `` ` `` / `**` / `*` 等行内标记)。
    - 「复制整行」:复制当前行各列文本(制表符分隔)。
    - 「复制整个表格」:复制表头与全部数据行(制表符分列、换行分行,不包含 `|` 与 `|:--|` 分隔行,缺列补空)。
  - 在表格行边缘右键,菜单包含「复制整行」「复制整个表格」。
- **文档选区复制(`Ctrl+A` / 跨段落拖选 + `Ctrl+C`)**:选区中包含的 Markdown 表格行自动转为 TSV 纯文本。
- `Editor` 开启 `IsDocumentEnabled="True"`,并由 `PreviewMouseRightButtonUp` / `ContextMenuOpening` 统一接管表格右键菜单(绕过 `RichTextBox` 内部 `FlowDocumentView.InputHitTestCore` 只返回 `Run` 的限制)。

## 组件

- `TableClipboard`(新增,纯逻辑):
  - `CellText(string)` → 去首尾空白的单元格文本。
  - `TableText(TableModel)` → TSV 文本,行尾不额外补换行。
- `EditableRenderer.BuildRowVisual`:
  - 每个单元格 `Border` 设置 `ContextMenu`(复制单元格 / 复制整个表格)。
  - 行容器 `Border` 设置 `ContextMenu`(复制整个表格)。
  - 菜单项点击后调用 `Clipboard.SetText`,异常忽略。
- 不新增设置项。

## 测试

- `TableClipboard.CellText`:首尾空白、普通文本、空文本。
- `TableClipboard.TableText`:2 列表格输出 `功能\t快捷键\r\n查找\tCtrl+F`;缺列补空;不含分隔行。
- 渲染:预览表格的行容器有右键菜单(1 项),每个单元格有右键菜单(2 项,文案正确)。
- 不写系统剪贴板的自动化测试(避免影响环境);剪贴板写入由手动验证。
- 既有 404 项测试保持通过;构建 0 警告 0 错误。

## 不在本次范围

- 鼠标拖选跨表格复制。
- 富文本/HTML 表格粘贴格式。

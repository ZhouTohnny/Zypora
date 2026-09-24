# Zypora 图片支持设计(粘贴 / 拖拽 / 预览显示)

- 日期:2026-09-23
- 状态:已与用户确认设计
- 范围:Zypora WPF Markdown 编辑器(`E:\MyProgram\Zypora`)

## 背景与不变量

核心不变量不变:**文档文本必须与 Markdown 源码逐字一致**(`EditableRenderer.ReadSource` 逐字等于源码),
以保证光标映射、命令与保存不损坏源码。

实测约束(WPF `TextRange`):
- `InlineUIContainer` 在文本中恰占 **1 个空格**;`BlockUIContainer` 额外多一个换行,不适用。
- 因此图片渲染沿用「隐藏 Run 前半段 + `InlineUIContainer` + 隐藏 Run 后半段,按**行内最后一个空格**拆分」的技巧。
- 推论:**图片行必须含至少一个空格**才能渲染为图片;否则回退为普通文本(与"紧凑表格"一致)。

## 目标 / 非目标

**目标**
1. 从剪贴板粘贴图片,自动复制副本到安装目录 `assets/`,并插入图片 Markdown。
2. 拖拽图片文件到编辑区,同样复制副本并插入。
3. 预览中显示图片(本地文件),保持文本恒等。
4. 副本文件名加随机值防冲突;原图移动/删除后文档仍能找到副本。

**非目标(YAGNI)**
- 图片上传/云存储、HTTP 图片下载、缩放/裁剪编辑、图注、按内容去重。

## 决策(已确认)

- 副本目录:**软件安装目录**下 `<AppContext.BaseDirectory>\assets\`(按需创建)。
- 未保存文档:粘贴/拖拽前**先提示保存文档**;用户取消则中止。
- 文件夹:**统一 `assets/`**;文件名加随机值防冲突。
- 路径写法:**绝对路径 + 正斜杠**,例如 `C:/Apps/Zypora/assets/photo_3f9a2c1b.png`。
- 渲染解析顺序(兜底):绝对路径 → 文档目录相对 → 安装目录相对 → 安装目录 `assets/` 按文件名。

## 新增 `ImageSupport.cs`(纯逻辑,可单测)

```csharp
public static class ImageSupport
{
    public static bool IsImageExtension(string path);
    // 整行(trim 后)形如 ![alt](src) 才成立;src 取括号内全部内容(trim)
    public static bool TryParseImageLine(string line, out string alt, out string src);
    // 生成副本文件名:安全化原名 + "_" + token + 扩展名;无原名 → clip_<token>.png
    public static string MakeAssetFileName(string? originalName, string token);
    // 解析可显示的本地路径;找不到或远程地址时原样返回
    public static string ResolveImagePath(string src, string? docDir, string appDir);
    public static bool IsRemote(string src);
}
```

- `IsImageExtension`:`.png .jpg .jpeg .gif .bmp`,大小写不敏感。
- `TryParseImageLine`:正则 `^!\[(.*?)\]\((.+)\)$` 作用于 `line.Trim()`。
- `MakeAssetFileName`:去掉非法文件名字符;无扩展名默认 `.png`。
- `ResolveImagePath`:先 `src.Replace('/', '\\')`;绝对且存在 → 返回;否则依次尝试
  `docDir`、`appDir`、`appDir/assets/<文件名>`;都不存在返回原 `src`。

## 粘贴 / 拖拽(`MainWindow.xaml.cs`)

- `DataObject.AddPastingHandler(Editor, OnPasting)`:
  - 剪贴板为图片(`Clipboard.ContainsImage`)→ 取 `BitmapImage` 编码 PNG。
  - 剪贴板为文件列表且含图片 → 复制这些文件。
  - 命中则 `e.CancelCommand()` 并执行插入;否则放行默认文本粘贴。
- `Editor.AllowDrop = true`;`PreviewDragOver` 设置 `e.Effects`;`PreviewDrop` 处理图片文件。
- 插入流程 `InsertImage(source)`:
  1. 若 `_currentFile == null` → `Save()`;取消则中止。
  2. 确保 `assets/` 存在。
  3. 生成 `MakeAssetFileName`,写入文件(剪贴板图片为 PNG 字节;文件则复制)。
  4. 计算绝对路径(正斜杠),生成 Markdown 行:
     `![image <n>](<abs>)`(`n` = 文档中下一个未用的 `image N` 计数,**保证含空格**)。
  5. 在光标处插入该行(必要时前后补换行),作为**一次撤销命令**(`_undo.Command`)。

## 预览渲染(`EditableRenderer`)

- `BuildLine` 开头:若 `preview` 且 `TryParseImageLine(trimmed,...)` 且该行含空格 →
  - 计算图片可视元素:`Image`(加载 `ResolveImagePath` 结果,`CacheOption=OnLoad`,`MaxWidth=640`,`Stretch=Uniform`,`StretchDirection=DownOnly`)。
  - 缺失/远程 → 占位 `Border`(浅灰底 + 路径文字)。
  - 按最后一个空格拆:`[隐藏 Run(line[0..lastSpace])] + [InlineUIContainer(图片)] + [隐藏 Run(line[lastSpace+1..])]`。
- 无空格图片行 / 行内混排 → 走原普通文本逻辑(显示 Markdown)。
- 原生模式(`BuildRaw`)不变,图片行即普通文本,可编辑。

## 新增 / 改动文件

| 文件 | 变更 |
|---|---|
| `ImageSupport.cs` | 新增(解析 / 路径解析 / 文件名) |
| `EditableRenderer.cs` | 图片行渲染 |
| `MainWindow.xaml.cs` | 粘贴、拖拽、插入、提示保存 |
| `使用说明.md` / `README.md` | 说明图片用法与限制 |

## 测试计划(追加到 `Zypora.Tests`)

- `ImageSupport`:扩展名判定、行解析(含空 alt / 前后空格 / 混排 false)、文件名(原名/无原名/非法字符/无扩展名)、路径解析顺序(用临时目录构造)。
- 渲染:图片行产生 `InlineUIContainer`;缺失图片产生占位;**恒等 fuzz 加入图片行**;光标映射往返。
- 全量回归(现有 147 项保持通过)。

## 风险与限制

- 安装目录只读(如 Program Files)→ 写 `assets/` 失败,弹错误提示。
- 移动 .md 到别的机器 → 绝对路径失效(同机可用;渲染有按文件名兜底)。
- 无空格的手写图片语法不渲染为图片(回退文本)。
- 图片不参与 PDF 导出(现有导出基于 Markdig,后续可另做)。

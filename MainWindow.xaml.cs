using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Zypora;

public enum ViewMode { Preview, Raw }

public partial class MainWindow : Window
{
    private static readonly System.Text.RegularExpressions.Regex UnorderedRegex =
        new(@"^\s*[-*+]\s+");
    private static readonly System.Text.RegularExpressions.Regex OrderedRegex =
        new(@"^\s*\d+[.)]\s+");
    private static readonly System.Text.RegularExpressions.Regex QuoteRegex =
        new(@"^\s*>\s?");

    private string? _currentFile;
    private string _savedText = "";
    private bool _dirty;
    private readonly DispatcherTimer _renderTimer = new();
    private readonly DispatcherTimer _groupTimer = new();
    private readonly UndoCoordinator _undo = new();
    private bool _suppressChanged;
    private bool _composing;
    private ViewMode _mode = ViewMode.Preview;
    private bool _renderQueued;
    private bool _rendering;

    private readonly List<(int Start, int Length)> _matches = new();
    private int _matchIndex = -1;
    private static readonly Brush MatchBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xA0));
    private static readonly Brush CurrentMatchBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47));

    public MainWindow()
    {
        InitializeComponent();

        _renderTimer.Interval = TimeSpan.FromMilliseconds(350);
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            ReRender();
        };

        _groupTimer.Interval = TimeSpan.FromMilliseconds(800);
        _groupTimer.Tick += (_, _) =>
        {
            _groupTimer.Stop();
            _undo.Commit();
        };

        TextCompositionManager.AddPreviewTextInputStartHandler(Editor, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(Editor, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(Editor, (_, _) =>
        {
            _composing = false;
            ScheduleRender();
        });

        DataObject.AddPastingHandler(Editor, OnPasting);
        Editor.AllowDrop = true;
        Editor.PreviewDragOver += OnDragOver;
        Editor.PreviewDrop += OnDrop;

        _mode = ViewMode.Preview;
        SetDocument(BuildForMode(LoadWelcomeText()));
        _savedText = GetSourceText();
        RefreshDirty();
        _undo.Reset(_savedText, 0);
        UpdateChrome();
    }

    private string GetSourceText() => EditableRenderer.ReadSource(Editor.Document);

    private static string LoadWelcomeText()
    {
        return "# 欢迎使用 Zypora\n\n这是一个 **Markdown** 所见即所得编辑器。\n\n## 支持的语法\n\n- **加粗** / *斜体* / `行内代码`\n- 标题(1~6 级)\n- 无序 / 有序列表\n- 代码块 / 引用 / 表格\n\n| 功能 | 快捷键 |\n|:--|:--|\n| 查找 | Ctrl+F |\n| 替换 | Ctrl+H |\n| 加粗 | Ctrl+B |\n\n> 直接在这里原地编辑,快捷键同 Typora。\n\n```csharp\nConsole.WriteLine(\"Hello, Markdown!\");\n```\n\n1. 试试 Ctrl+B 加粗\n2. 试试 Ctrl+1 转标题\n3. 点击 **导出 PDF**\n";
    }

    private void SetDocument(FlowDocument doc)
    {
        _suppressChanged = true;
        Editor.Document = doc;
        _suppressChanged = false;
        RefreshDirty();
    }

    private void RefreshDirty()
    {
        bool dirty = GetSourceText() != _savedText;
        if (dirty != _dirty)
        {
            _dirty = dirty;
            UpdateChrome();
        }
    }

    private void ScheduleRender()
    {
        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressChanged || _composing) return;
        if (_mode == ViewMode.Preview)
        {
            QueueRender();
        }
        else
        {
            ScheduleRender();
        }
    }

    private void QueueRender()
    {
        if (_renderQueued || _rendering) return;
        _renderQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _renderQueued = false;
            ReRender();
        }));
    }

    private FlowDocument BuildForMode(string source) => _mode switch
    {
        ViewMode.Raw => EditableRenderer.BuildRaw(source),
        _ => EditableRenderer.BuildPreview(source, DocDir, AppContext.BaseDirectory),
    };

    private string? DocDir => _currentFile == null ? null : Path.GetDirectoryName(_currentFile);

    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "assets");

    private void ReRender()
    {
        if (_rendering) return;
        _rendering = true;
        try
        {
            _renderTimer.Stop();
            var source = GetSourceText();
            int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
            SetDocument(BuildForMode(source));
            Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, caret);
            _undo.Change(GetSourceText(), caret);
            _groupTimer.Stop();
            _groupTimer.Start();
            if (FindBar.Visibility == Visibility.Visible)
            {
                RecomputeMatches();
                PaintHighlights();
                UpdateMatchLabel();
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private void ApplyLineEdit(string newText, int newCaret)
    {
        SetDocument(BuildForMode(newText));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, newCaret);
        _undo.Command(GetSourceText(), newCaret);
        Editor.Focus();
    }

    private void Restore((string Text, int Caret) state)
    {
        _renderTimer.Stop();
        _groupTimer.Stop();
        SetDocument(BuildForMode(state.Text));
        int caret = Math.Clamp(state.Caret, 0, state.Text.Length);
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, caret);
        Editor.Focus();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        _groupTimer.Stop();
        var caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        var state = _undo.Undo(GetSourceText(), caret);
        if (state != null) Restore(state.Value);
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        _groupTimer.Stop();
        var state = _undo.Redo();
        if (state != null) Restore(state.Value);
    }

    private void OnTogglePreview(object sender, RoutedEventArgs e) => CycleMode();

    private void CycleMode() => SetMode(_mode == ViewMode.Preview ? ViewMode.Raw : ViewMode.Preview);

    private void SetMode(ViewMode mode)
    {
        if (_mode == mode) return;

        _renderTimer.Stop();
        var source = GetSourceText();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        _mode = mode;
        SetDocument(BuildForMode(source));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, caret);
        UpdateChrome();
        Editor.Focus();
    }

    private void UpdateChrome()
    {
        ModeButton.Content = _mode == ViewMode.Preview ? "原生" : "预览";
        var name = _currentFile == null ? "未命名" : Path.GetFileName(_currentFile);
        var star = _dirty ? "*" : "";
        var mode = _mode == ViewMode.Preview ? "预览" : "原生";
        Title = $"{star}{name} - Zypora ({mode})";
    }

    private void ToggleInline(string marker, bool requireSelection = false)
    {
        var source = GetSourceText();
        int start = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.Start);
        int end = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.End);
        if (start > end) (start, end) = (end, start);

        if (start == end)
        {
            if (requireSelection) return; // 加粗/斜体:未选中内容时不生效

            var (itext, icaret) = InlineEditor.ToggleAtCaret(source, start, marker);
            SetDocument(BuildForMode(itext));
            Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(icaret, 0, itext.Length));
            _undo.Command(GetSourceText(), icaret);
            Editor.Focus();
            return;
        }

        if (marker == "`" && source.Substring(start, end - start).Contains('\n'))
        {
            var (ftext, fs, fe) = InlineEditor.WrapFence(source, start, end);
            SetDocument(BuildForMode(ftext));
            var fp1 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(fs, 0, ftext.Length));
            var fp2 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(fe, 0, ftext.Length));
            Editor.Selection.Select(fp1, fp2);
            _undo.Command(GetSourceText(), fe);
            Editor.Focus();
            return;
        }

        var (text, selStart, selEnd) = InlineEditor.Toggle(source, start, end, marker);

        SetDocument(BuildForMode(text));
        var sPtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selStart, 0, text.Length));
        var ePtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selEnd, 0, text.Length));
        Editor.Selection.Select(sPtr, ePtr);
        _undo.Command(GetSourceText(), selEnd);
        Editor.Focus();
    }

    private (string Source, int Caret) CurrentState()
    {
        return (GetSourceText(), DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition));
    }

    private void ToggleLinePrefix(string mark, System.Text.RegularExpressions.Regex regex)
    {
        var source = GetSourceText();
        int start = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.Start);
        int end = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.End);
        if (start > end) (start, end) = (end, start);

        var (text, selStart, selEnd) = LineEditor.TogglePrefixRange(source, start, end, mark, regex, regex);

        SetDocument(BuildForMode(text));
        var sPtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selStart, 0, text.Length));
        var ePtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selEnd, 0, text.Length));
        Editor.Selection.Select(sPtr, ePtr);
        _undo.Command(GetSourceText(), selEnd);
        Editor.Focus();
    }

    private void ToggleHeading(int level)
    {
        var (source, caret) = CurrentState();
        var (text, newCaret) = LineEditor.ToggleHeading(source, caret, level);
        ApplyLineEdit(text, newCaret);
    }

    private bool TryContinueList()
    {
        var source = GetSourceText();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        var (handled, text, newCaret) = LineEditor.HandleListEnter(source, caret);
        if (!handled) return false;

        SetDocument(BuildForMode(text));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(newCaret, 0, text.Length));
        _undo.Command(GetSourceText(), newCaret);
        Editor.Focus();
        return true;
    }

    // ---- Toolbar / shortcut handlers ----

    private void OnBold(object sender, RoutedEventArgs e) => ToggleInline("**", requireSelection: true);

    private void OnItalic(object sender, RoutedEventArgs e) => ToggleInline("*", requireSelection: true);

    private void OnCode(object sender, RoutedEventArgs e) => ToggleInline("`");

    private void OnCodeBlock(object sender, RoutedEventArgs e)
    {
        var source = GetSourceText();
        int start = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.Start);
        int end = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.End);

        var (text, selStart, selEnd) = InlineEditor.ToggleCodeBlock(source, start, end);

        SetDocument(BuildForMode(text));
        var sPtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selStart, 0, text.Length));
        var ePtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selEnd, 0, text.Length));
        Editor.Selection.Select(sPtr, ePtr);
        _undo.Command(GetSourceText(), selEnd);
        Editor.Focus();
    }

    private void OnList(object sender, RoutedEventArgs e) => ToggleLinePrefix("- ", UnorderedRegex);

    private void OnOrderedList(object sender, RoutedEventArgs e) => ToggleLinePrefix("1. ", OrderedRegex);

    private void OnInsertTable(object sender, RoutedEventArgs e) => InsertTable();

    private void InsertTable()
    {
        var source = GetSourceText();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        var (text, selStart, selEnd) = TableParser.InsertTemplate(source, caret);

        SetDocument(BuildForMode(text));
        var p1 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selStart, 0, text.Length));
        var p2 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selEnd, 0, text.Length));
        Editor.Selection.Select(p1, p2);
        _undo.Command(GetSourceText(), selEnd);
        Editor.Focus();
    }

    private void OnQuote(object sender, RoutedEventArgs e) => ToggleLinePrefix("> ", QuoteRegex);

    private void OnHeading1(object sender, RoutedEventArgs e) => ToggleHeading(1);

    private void OnHeading2(object sender, RoutedEventArgs e) => ToggleHeading(2);

    // ---- File / export ----

    // 新建:开一个新窗口,便于同时编辑多个文件
    private static int _cascadeIndex;

    private MainWindow CreateNewWindow()
    {
        var w = new MainWindow();
        w.ClearDocument();

        // 相对当前窗口做阶梯偏移,避免与当前窗口完全重合
        if (double.IsNaN(Left) || double.IsNaN(Top) || WindowState != WindowState.Normal)
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return w;
        }

        _cascadeIndex = (_cascadeIndex + 1) % 8;
        double delta = 26 * (_cascadeIndex + 1);
        var wa = SystemParameters.WorkArea;

        double left = Math.Max(wa.Left, Math.Min(Left + delta, wa.Right - w.Width));
        double top = Math.Max(wa.Top, Math.Min(Top + delta, wa.Bottom - w.Height));

        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = left;
        w.Top = top;
        return w;
    }

    private void OnNewWindow(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        CreateNewWindow().Show();
    }

    // 清空:把当前文档重置为空白
    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        ClearDocument();
    }

    private void ClearDocument()
    {
        _renderTimer.Stop();
        _groupTimer.Stop();
        SetDocument(BuildForMode(""));
        _savedText = GetSourceText();
        RefreshDirty();
        _undo.Reset(_savedText, 0);
        _currentFile = null;
        UpdateChrome();
        Editor.Focus();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;

        var dlg = new OpenFileDialog
        {
            Filter = "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);
            _currentFile = dlg.FileName;
            _renderTimer.Stop();
            _groupTimer.Stop();
            SetDocument(BuildForMode(text));
            _savedText = GetSourceText();
            RefreshDirty();
            _undo.Reset(_savedText, 0);
            UpdateChrome();
            Editor.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开失败:\n" + ex.Message, "Zypora", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    private bool Save()
    {
        if (_currentFile == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Markdown 文件 (*.md)|*.md",
                FileName = "未命名.md",
            };
            if (dlg.ShowDialog() != true) return false;
            _currentFile = dlg.FileName;
        }

        try
        {
            var text = GetSourceText();
            File.WriteAllText(_currentFile, text);
            _savedText = text;
            RefreshDirty();
            UpdateChrome();
            MessageBox.Show("已保存。", "Zypora", MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败:\n" + ex.Message, "Zypora", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    // 有未保存改动时询问;返回 false 表示用户取消(应中止当前操作)
    private bool ConfirmDiscard()
    {
        if (GetSourceText() == _savedText) return true;

        var result = MessageBox.Show(
            "当前文档尚未保存,是否保存更改?", "Zypora",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Cancel => false,
            MessageBoxResult.Yes => Save(),
            _ => true,
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    private void OnExportPdf(object sender, RoutedEventArgs e)
    {
        var save = new SaveFileDialog
        {
            Filter = "PDF 文件 (*.pdf)|*.pdf",
            FileName = _currentFile == null ? "文档.pdf" : Path.GetFileNameWithoutExtension(_currentFile) + ".pdf",
        };
        if (save.ShowDialog() != true) return;

        try
        {
            var bytes = PdfExporter.Export(GetSourceText());
            File.WriteAllBytes(save.FileName, bytes);
            MessageBox.Show("PDF 已导出。", "Zypora", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败:\n" + ex.Message, "Zypora", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ---- 图片:粘贴 / 拖拽 / 插入 ----

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (e.SourceDataObject.GetDataPresent(DataFormats.Bitmap) &&
                e.SourceDataObject.GetData(DataFormats.Bitmap) is BitmapSource)
            {
                e.CancelCommand();
                var img = Clipboard.GetImage();
                if (img != null) InsertClipboardImage(img);
                return;
            }

            if (e.SourceDataObject.GetDataPresent(DataFormats.FileDrop))
            {
                var files = e.SourceDataObject.GetData(DataFormats.FileDrop) as string[];
                var images = files?.Where(ImageSupport.IsImageExtension).ToArray();
                if (images is { Length: > 0 })
                {
                    e.CancelCommand();
                    InsertImageFiles(images);
                }
            }
        }
        catch (Exception ex)
        {
            ShowError("粘贴图片失败", ex);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var images = files?.Where(ImageSupport.IsImageExtension).ToArray();
        if (images is not { Length: > 0 }) return;

        e.Handled = true;
        InsertImageFiles(images);
    }

    // 未保存文档先提示保存(用户取消则中止)
    private bool EnsureSavedForImage() => _currentFile != null || Save();

    private void InsertClipboardImage(BitmapSource img)
    {
        if (!EnsureSavedForImage()) return;
        try
        {
            var path = ImageSupport.SaveBitmapToAssets(img, AssetsDir, NewToken());
            InsertImageMarkdown(new[] { path });
        }
        catch (Exception ex)
        {
            ShowError("保存图片副本失败", ex);
        }
    }

    private void InsertImageFiles(IReadOnlyList<string> files)
    {
        if (!EnsureSavedForImage()) return;
        var copied = new List<string>();
        try
        {
            foreach (var f in files)
            {
                copied.Add(ImageSupport.CopyFileToAssets(f, AssetsDir, NewToken()));
            }
        }
        catch (Exception ex)
        {
            ShowError("复制图片失败", ex);
        }

        if (copied.Count > 0) InsertImageMarkdown(copied);
    }

    private void InsertImageMarkdown(IReadOnlyList<string> absolutePaths)
    {
        var source = GetSourceText();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        var (text, newCaret) = ImageSupport.InsertImageLines(source, caret, absolutePaths);
        ApplyCommandText(text, newCaret);
    }

    private static string NewToken() => Guid.NewGuid().ToString("N").Substring(0, 8);

    private static void ShowError(string title, Exception ex)
        => MessageBox.Show(title + ":\n" + ex.Message, "Zypora", MessageBoxButton.OK, MessageBoxImage.Error);

    // ---- 查找 / 替换 ----

    private SearchOptions CurrentSearchOptions()
        => new(CaseBox.IsChecked == true, WholeWordBox.IsChecked == true, RegexBox.IsChecked == true);

    private void ShowFind(bool replace)
    {
        FindBar.Visibility = Visibility.Visible;
        ReplaceBox.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButton.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceAllButton.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        FindBox.Focus();
        FindBox.SelectAll();
        RebuildForSearch(selectCurrent: true);
    }

    private void OnFindTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (FindBar.Visibility != Visibility.Visible) return;
        RebuildForSearch(selectCurrent: false);
    }

    private void OnFindOptionChanged(object sender, RoutedEventArgs e)
    {
        if (FindBar.Visibility != Visibility.Visible) return;
        RebuildForSearch(selectCurrent: true);
    }

    private void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return) { MoveMatch(1); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseFind(); e.Handled = true; }
    }

    private void OnReplaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return) { ReplaceCurrent(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseFind(); e.Handled = true; }
    }

    private void OnFindNext(object sender, RoutedEventArgs e) => MoveMatch(1);

    private void OnFindPrev(object sender, RoutedEventArgs e) => MoveMatch(-1);

    private void OnFindClose(object sender, RoutedEventArgs e) => CloseFind();

    // 重建文档以清除旧高亮,再重新计算并上色
    private void RebuildForSearch(bool selectCurrent)
    {
        var source = GetSourceText();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        SetDocument(BuildForMode(source));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, caret);

        RecomputeMatches();
        PaintHighlights();
        UpdateMatchLabel();
        if (selectCurrent && _matchIndex >= 0) SelectMatch(_matchIndex);
    }

    private void RecomputeMatches()
    {
        _matches.Clear();
        _matches.AddRange(SearchService.Find(GetSourceText(), FindBox.Text, CurrentSearchOptions()));
        _matchIndex = _matches.Count == 0 ? -1 : Math.Clamp(_matchIndex, 0, _matches.Count - 1);
        if (_matches.Count > 0 && _matchIndex < 0) _matchIndex = 0;
    }

    private void MoveMatch(int delta)
    {
        if (_matches.Count == 0) { UpdateMatchLabel(); return; }
        _matchIndex = (_matchIndex + delta + _matches.Count) % _matches.Count;
        PaintHighlights();
        UpdateMatchLabel();
        SelectMatch(_matchIndex);
    }

    private void PaintHighlights()
    {
        _suppressChanged = true;
        try
        {
            for (int i = 0; i < _matches.Count; i++)
            {
                var (start, len) = _matches[i];
                var p1 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, start);
                var p2 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, start + len);
                var range = new TextRange(p1, p2);
                range.ApplyPropertyValue(TextElement.BackgroundProperty, i == _matchIndex ? CurrentMatchBrush : MatchBrush);
            }
        }
        finally
        {
            _suppressChanged = false;
        }
    }

    private void SelectMatch(int index)
    {
        var (start, len) = _matches[index];
        var p1 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, start);
        var p2 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, start + len);
        Editor.Selection.Select(p1, p2);
        // 借编辑器聚焦触发滚动到选区,随后把焦点交还查找框
        Editor.Focus();
        Editor.CaretPosition = p2;
        FindBox.Focus();
    }

    private void UpdateMatchLabel()
    {
        if (string.IsNullOrEmpty(FindBox.Text)) MatchLabel.Text = "";
        else if (_matches.Count == 0) MatchLabel.Text = "无匹配";
        else MatchLabel.Text = $"{_matchIndex + 1}/{_matches.Count}";
    }

    private void OnReplace(object sender, RoutedEventArgs e) => ReplaceCurrent();

    private void ReplaceCurrent()
    {
        if (_matchIndex < 0 || _matchIndex >= _matches.Count) return;

        var (text, caret) = SearchService.ReplaceOne(GetSourceText(), _matches[_matchIndex], ReplaceBox.Text);
        ApplyCommandText(text, caret);
        RebuildForSearch(selectCurrent: true);
    }

    private void OnReplaceAll(object sender, RoutedEventArgs e)
    {
        var (text, count) = SearchService.ReplaceAll(GetSourceText(), FindBox.Text, ReplaceBox.Text, CurrentSearchOptions());
        if (count > 0) ApplyCommandText(text, 0);
        RebuildForSearch(selectCurrent: false);
        MatchLabel.Text = count > 0 ? $"替换 {count} 处" : "无匹配";
    }

    private void ApplyCommandText(string newText, int newCaret)
    {
        _renderTimer.Stop();
        _groupTimer.Stop();
        SetDocument(BuildForMode(newText));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(newCaret, 0, newText.Length));
        _undo.Command(GetSourceText(), newCaret);
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        _matches.Clear();
        _matchIndex = -1;
        MatchLabel.Text = "";

        var source = GetSourceText();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        SetDocument(BuildForMode(source));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, caret);
        Editor.Focus();
    }

    // ---- keyboard shortcuts (Typora style) ----

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // 空格/回车作为输入组边界:先收尾当前输入组
        if (!ctrl && !shift && (e.Key == Key.Space || e.Key == Key.Enter || e.Key == Key.Return))
        {
            _undo.Commit();
        }

        if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
        {
            CloseFind();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.F) { ShowFind(false); e.Handled = true; return; }
        if (ctrl && e.Key == Key.H) { ShowFind(true); e.Handled = true; return; }

        if ((e.Key == Key.Enter || e.Key == Key.Return) && !ctrl && !shift && !_composing && _mode != ViewMode.Raw && Editor.Selection.IsEmpty)
        {
            if (TryContinueList()) { e.Handled = true; return; }
        }

        if (ctrl && e.Key == Key.B) { OnBold(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.I) { OnItalic(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.K) { OnCodeBlock(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.T) { OnInsertTable(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.K) { OnCode(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && (e.Key == Key.OemQuestion || e.Key == Key.Oem2)) { OnTogglePreview(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z && !shift) { OnUndo(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { OnRedo(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.Z) { OnRedo(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.D7) { OnList(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.O) { OnQuote(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.D1) { OnHeading1(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.D2) { OnHeading2(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { OnSave(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.N) { OnClear(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { OnNewWindow(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { OnOpen(this, new RoutedEventArgs()); e.Handled = true; }

        base.OnPreviewKeyDown(e);
    }
}

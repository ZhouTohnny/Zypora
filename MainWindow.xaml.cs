using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly DispatcherTimer _renderTimer = new();
    private readonly UndoHistory _history = new();
    private bool _suppressChanged;
    private bool _composing;
    private ViewMode _mode = ViewMode.Preview;
    private bool _renderQueued;
    private bool _rendering;

    public MainWindow()
    {
        InitializeComponent();

        _renderTimer.Interval = TimeSpan.FromMilliseconds(350);
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            ReRender();
        };

        TextCompositionManager.AddPreviewTextInputStartHandler(Editor, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(Editor, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(Editor, (_, _) =>
        {
            _composing = false;
            ScheduleRender();
        });

        _mode = ViewMode.Preview;
        SetDocument(BuildForMode(LoadWelcomeText()));
        _history.Reset(GetSourceText(), 0);
        UpdateChrome();
    }

    private string GetSourceText() => EditableRenderer.ReadSource(Editor.Document);

    private static string LoadWelcomeText()
    {
        return "# 欢迎使用 Zypora\n\n这是一个 **Markdown** 所见即所得编辑器。\n\n## 支持的语法\n\n- **加粗** / *斜体* / `行内代码`\n- 标题(1~4 级)\n- 无序 / 有序列表\n- 代码块 / 引用\n\n> 直接在这里原地编辑,快捷键同 Typora。\n\n```csharp\nConsole.WriteLine(\"Hello, Markdown!\");\n```\n\n1. 试试 Ctrl+B 加粗\n2. 试试 Ctrl+1 转标题\n3. 点击 **导出 PDF**\n";
    }

    private void SetDocument(FlowDocument doc)
    {
        _suppressChanged = true;
        Editor.Document = doc;
        _suppressChanged = false;
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
        _ => EditableRenderer.BuildPreview(source),
    };

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
            _history.Push(GetSourceText(), caret);
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
        _history.Push(GetSourceText(), newCaret);
        Editor.Focus();
    }

    private void Restore((string Text, int Caret) state)
    {
        _renderTimer.Stop();
        SetDocument(BuildForMode(state.Text));
        int caret = Math.Clamp(state.Caret, 0, state.Text.Length);
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, caret);
        Editor.Focus();
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        var state = _history.Undo();
        if (state != null) Restore(state.Value);
    }

    private void OnRedo(object sender, RoutedEventArgs e)
    {
        var state = _history.Redo();
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
        Title = _mode == ViewMode.Preview ? "Zypora - 预览" : "Zypora - 原生";
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
            _history.Push(GetSourceText(), icaret);
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
            _history.Push(GetSourceText(), fe);
            Editor.Focus();
            return;
        }

        var (text, selStart, selEnd) = InlineEditor.Toggle(source, start, end, marker);

        SetDocument(BuildForMode(text));
        var sPtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selStart, 0, text.Length));
        var ePtr = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selEnd, 0, text.Length));
        Editor.Selection.Select(sPtr, ePtr);
        _history.Push(GetSourceText(), selEnd);
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
        _history.Push(GetSourceText(), selEnd);
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
        _history.Push(GetSourceText(), newCaret);
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
        _history.Push(GetSourceText(), selEnd);
        Editor.Focus();
    }

    private void OnList(object sender, RoutedEventArgs e) => ToggleLinePrefix("- ", UnorderedRegex);

    private void OnOrderedList(object sender, RoutedEventArgs e) => ToggleLinePrefix("1. ", OrderedRegex);

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

    private void OnNewWindow(object sender, RoutedEventArgs e) => CreateNewWindow().Show();

    // 清空:把当前文档重置为空白
    private void OnClear(object sender, RoutedEventArgs e) => ClearDocument();

    private void ClearDocument()
    {
        _renderTimer.Stop();
        SetDocument(BuildForMode(""));
        _history.Reset("", 0);
        _currentFile = null;
        UpdateChrome();
        Editor.Focus();
    }

    private void OnOpen(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var text = File.ReadAllText(dlg.FileName);
            _currentFile = dlg.FileName;
            SetDocument(BuildForMode(text));
            _history.Reset(GetSourceText(), 0);
            UpdateChrome();
            Editor.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show("打开失败:\n" + ex.Message, "Zypora", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_currentFile == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Markdown 文件 (*.md)|*.md",
                FileName = "未命名.md",
            };
            if (dlg.ShowDialog() != true) return;
            _currentFile = dlg.FileName;
        }

        try
        {
            File.WriteAllText(_currentFile, GetSourceText());
            UpdateChrome();
            MessageBox.Show("已保存。", "Zypora", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败:\n" + ex.Message, "Zypora", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    // ---- keyboard shortcuts (Typora style) ----

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if ((e.Key == Key.Enter || e.Key == Key.Return) && !ctrl && !shift && !_composing && _mode != ViewMode.Raw && Editor.Selection.IsEmpty)
        {
            if (TryContinueList()) { e.Handled = true; return; }
        }

        if (ctrl && e.Key == Key.B) { OnBold(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && e.Key == Key.I) { OnItalic(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ctrl && shift && e.Key == Key.K) { OnCodeBlock(this, new RoutedEventArgs()); e.Handled = true; }
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

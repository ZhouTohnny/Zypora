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
    private string _welcomeText = "";
    private bool _dirty;
    private bool _skipClosingPrompt;
    private readonly DispatcherTimer _renderTimer = new();
    private readonly DispatcherTimer _groupTimer = new();
    private readonly UndoCoordinator _undo = new();
    private bool _suppressChanged;
    private bool _composing;
    private ViewMode _mode = ViewMode.Preview;
    private bool _renderQueued;
    private bool _rendering;
    private double _fitWidth;

    private AppSettings _settings = new();
    private AppTheme _theme = AppTheme.Light;

    // ---- 手动检查更新 ----
    private bool _checkingUpdate;
    private readonly DispatcherTimer _statusTimer = new();

    // ---- 阅读模式状态 ----
    private bool _reading;
    private WindowStyle _styleBeforeReading;
    private ResizeMode _resizeBeforeReading;
    private Visibility _findBarBeforeReading = Visibility.Collapsed;
    private readonly DispatcherTimer _scrollBarTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private System.Windows.Controls.Primitives.ScrollBar? _readingScrollBar;

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
        Editor.SizeChanged += (_, _) =>
        {
            if (Math.Abs(Editor.ActualWidth - _fitWidth) < 8) return;
            _fitWidth = Editor.ActualWidth;
            FitContentToWidth();
        };
        Editor.AddHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent,
            new System.Windows.Controls.ScrollChangedEventHandler((_, e) =>
            {
                if (Math.Abs(e.ViewportWidthChange) > 4) FitContentToWidth();
                if (_reading && Math.Abs(e.VerticalChange) > 0.01) ShowReadingScrollBarTemporarily();
            }));
        _scrollBarTimer.Tick += (_, _) => HideReadingScrollBar();

        _mode = ViewMode.Preview;
        SetDocument(BuildForMode(LoadWelcomeText()));
        _savedText = GetSourceText();
        _welcomeText = _savedText;
        RefreshDirty();
        _undo.Reset(_savedText, 0);
        UpdateChrome();

        _statusTimer.Tick += (_, _) => HideUpdateStatus();
        VersionInfoItem.Header = "版本 v" + UpdateService.CurrentVersion();

        _settings = SettingsStore.Load(SettingsStore.DefaultDir);
        _theme = _settings.ResolveTheme();
        ApplyTheme();
        UpdateThemeMenu();
    }

    private string GetSourceText() => EditableRenderer.ReadSource(Editor.Document);

    private static string LoadWelcomeText()
    {
        return "# 欢迎使用 Zypora\n\n这是一个 **Markdown** 所见即所得编辑器。\n\n## 支持的语法\n\n- **加粗** / *斜体* / `行内代码`\n- 标题(1~6 级)\n- 无序 / 有序列表\n- 代码块 / 引用 / 表格\n\n| 功能 | 快捷键 |\n|:--|:--|\n| 查找 | Ctrl+F |\n| 替换 | Ctrl+H |\n| 加粗 | Ctrl+B |\n\n> 直接在这里原地编辑,快捷键同 Typora。\n\n```csharp\nConsole.WriteLine(\"Hello, Markdown!\");\n```\n\n1. 试试 Ctrl+B 加粗\n2. 试试 Ctrl+1 转标题\n3. 点击 **导出 PDF**\n";
    }

    private void SetDocument(FlowDocument doc)
    {
        _renderTimer.Stop();
        _renderQueued = false;
        _suppressChanged = true;
        Editor.Document = doc;
        _suppressChanged = false;
        FitContentToWidth();
        RefreshDirty();
    }

    /// <summary>把表格/图片按编辑区宽度适配,避免内容超出窗口被裁掉。</summary>
    private void FitContentToWidth()
    {
        var doc = Editor.Document;
        if (doc == null) return;

        double viewport = GetViewportWidth();
        if (viewport <= 50) return; // 布局尚未完成,等 SizeChanged 再处理

        double available = viewport - doc.PagePadding.Left - doc.PagePadding.Right;
        if (available < 120) available = 120;
        EditableRenderer.FitContent(doc, available);
    }

    private double GetViewportWidth()
    {
        Editor.ApplyTemplate();
        if (Editor.Template?.FindName("PART_ContentHost", Editor) is System.Windows.Controls.ScrollViewer sv && sv.ViewportWidth > 1)
        {
            return sv.ViewportWidth;
        }
        double width = Editor.ActualWidth - Editor.BorderThickness.Left - Editor.BorderThickness.Right;
        return width > 1 ? width : Math.Max(1, Editor.ActualWidth);
    }

    private bool HasUnsavedChanges()
    {
        var current = GetSourceText();
        if (current == _savedText) return false;
        if (_currentFile == null && current == _welcomeText) return false;
        return true;
    }

    private void RefreshDirty()
    {
        bool dirty = HasUnsavedChanges();
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
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        _undo.Change(GetSourceText(), caret);
        _groupTimer.Stop();
        _groupTimer.Start();
        RefreshDirty();

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
            if (!_renderQueued) return;
            _renderQueued = false;
            ReRender();
        }));
    }

    private FlowDocument BuildForMode(string source)
    {
        var theme = RenderTheme.Of(_theme);
        return _mode switch
        {
            ViewMode.Raw => EditableRenderer.BuildRaw(source, theme),
            _ => EditableRenderer.BuildPreview(source, DocDir, AppContext.BaseDirectory, theme),
        };
    }

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
            int selStart = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.Start);
            int selEnd = DocumentCaret.GetCaretOffset(Editor.Document, Editor.Selection.End);
            int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
            SetDocument(BuildForMode(source));
            if (selStart != selEnd)
            {
                var p1 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selStart, 0, source.Length));
                var p2 = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(selEnd, 0, source.Length));
                Editor.Selection.Select(p1, p2);
            }
            else
            {
                Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(caret, 0, source.Length));
            }
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
        UpdateModeMenu();
        Title = "Zypora";
    }

    // ---- 模式下拉(预览 / 原生 / 阅读) ----

    private void OnModeMenu(object sender, RoutedEventArgs e)
    {
        var cm = ModeMenuButton.ContextMenu;
        if (cm == null) return;
        cm.PlacementTarget = ModeMenuButton;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void OnSelectMode(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item || item.Tag is not string tag) return;

        switch (tag)
        {
            case "raw":
                SetMode(ViewMode.Raw);
                break;
            case "read":
                EnterReadingMode();
                break;
            default:
                SetMode(ViewMode.Preview);
                break;
        }
    }

    private void UpdateModeMenu()
    {
        MarkMenuItem(ModePreviewItem, _mode == ViewMode.Preview, "预览");
        MarkMenuItem(ModeRawItem, _mode == ViewMode.Raw, "原生");
        MarkMenuItem(ModeReadItem, false, "阅读");
    }

    private static void MarkMenuItem(System.Windows.Controls.MenuItem item, bool current, string label)
    {
        item.Header = (current ? "✓ " : "    ") + label;
        item.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
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

    private void OnToggleTopmost(object sender, RoutedEventArgs e)
        => Topmost = TopmostButton.IsChecked == true;

    private void OnFileMenu(object sender, RoutedEventArgs e)
    {
        var cm = FileMenuButton.ContextMenu;
        if (cm == null) return;
        cm.PlacementTarget = FileMenuButton;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void OnFormatMenu(object sender, RoutedEventArgs e)
    {
        var cm = FormatMenuButton.ContextMenu;
        if (cm == null) return;
        cm.PlacementTarget = FormatMenuButton;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void OnThemeMenu(object sender, RoutedEventArgs e)
    {
        var cm = ThemeMenuButton.ContextMenu;
        if (cm == null) return;
        cm.PlacementTarget = ThemeMenuButton;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private void OnSelectTheme(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item || item.Tag is not string code) return;

        var theme = AppThemeCodes.Parse(code);
        if (theme == _theme)
        {
            UpdateThemeMenu();
            return;
        }

        _theme = theme;
        ApplyTheme();
        UpdateThemeMenu();

        var s = SettingsStore.Load(SettingsStore.DefaultDir);
        s.Theme = AppThemeCodes.ToCode(_theme);
        s.DarkMode = _theme == AppTheme.Dark;
        SettingsStore.Save(SettingsStore.DefaultDir, s);
        _settings = s;
    }

    private void UpdateThemeMenu()
    {
        MarkMenuItem(ThemeLightItem, _theme == AppTheme.Light, AppThemeCodes.Label(AppTheme.Light));
        MarkMenuItem(ThemeDarkItem, _theme == AppTheme.Dark, AppThemeCodes.Label(AppTheme.Dark));
        MarkMenuItem(ThemeEyeItem, _theme == AppTheme.Eye, AppThemeCodes.Label(AppTheme.Eye));
    }

    private void ApplyTheme()
    {
        var c = ChromeTheme.Of(_theme);
        var res = Resources;
        res["Br.WindowBg"] = new SolidColorBrush(c.WindowBg);
        res["Br.PanelBg"] = new SolidColorBrush(c.PanelBg);
        res["Br.FindBg"] = new SolidColorBrush(c.FindBg);
        res["Br.PanelBorder"] = new SolidColorBrush(c.PanelBorder);
        res["Br.Fg"] = new SolidColorBrush(c.Fg);
        res["Br.Muted"] = new SolidColorBrush(c.Muted);
        res["Br.Hover"] = new SolidColorBrush(c.Hover);
        res["Br.Checked"] = new SolidColorBrush(c.Checked);
        res["Br.CheckedFg"] = new SolidColorBrush(c.CheckedFg);
        res["Br.MenuBg"] = new SolidColorBrush(c.MenuBg);
        res["Br.MenuFg"] = new SolidColorBrush(c.MenuFg);
        res["Br.MenuHover"] = new SolidColorBrush(c.MenuHover);
        res["Br.MenuBorder"] = new SolidColorBrush(c.MenuBorder);

        Editor.Background = new SolidColorBrush(c.EditorBg);
        Editor.CaretBrush = new SolidColorBrush(c.Caret);

        // 用新主题重建文档
        var source = GetSourceText();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
        SetDocument(BuildForMode(source));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(caret, 0, source.Length));
    }

    // ---- 阅读模式(沉浸式) ----

    private void OnEnterReading(object sender, RoutedEventArgs e) => EnterReadingMode();

    private void OnExitReading(object sender, RoutedEventArgs e) => ExitReadingMode();

    private void EnterReadingMode()
    {
        if (_reading) return;
        _reading = true;

        _styleBeforeReading = WindowStyle;
        _resizeBeforeReading = ResizeMode;
        _findBarBeforeReading = FindBar.Visibility;

        double vScroll = GetVerticalScrollOffset();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);

        // 强制切到预览并重建文档(同时清除可能残留的查找高亮)
        if (_mode != ViewMode.Preview || _findBarBeforeReading == Visibility.Visible)
        {
            _mode = ViewMode.Preview;
            UpdateModeMenu();
            var source = GetSourceText();
            SetDocument(BuildForMode(source));
            Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(caret, 0, source.Length));
        }

        Editor.IsReadOnly = true;
        FindBar.Visibility = Visibility.Collapsed;
        ToolbarBorder.Visibility = Visibility.Collapsed;
        ExitReadingButton.Visibility = Visibility.Visible;
        InitReadingScrollBar();

        // 无边框 + 顶部按住可拖动 + 边缘可拖动调整大小(用 WindowChrome,不画任何边框)
        ResizeMode = ResizeMode.CanResize;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        WindowStyle = WindowStyle.None;
        SetVerticalScrollOffset(vScroll);
        Editor.Focus();
    }

    private void ExitReadingMode()
    {
        if (!_reading) return;
        _reading = false;

        double vScroll = GetVerticalScrollOffset();
        int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);

        ExitReadingButton.Visibility = Visibility.Collapsed;
        ResetReadingScrollBar();
        ToolbarBorder.Visibility = Visibility.Visible;
        FindBar.Visibility = _findBarBeforeReading;

        // 顺序很关键:必须先恢复 WindowStyle/ResizeMode,再清除 WindowChrome。
        // 反过来的话,WPF 会在窗口还是 None 样式时清理圆角区域,之后恢复样式又把
        // DWM 外框状态搞脏 —— 表现为退出阅读模式后标题栏退化成"经典"灰蓝外框。
        WindowStyle = _styleBeforeReading;
        ResizeMode = _resizeBeforeReading;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, null);

        Editor.IsReadOnly = false;
        _mode = ViewMode.Preview;
        UpdateModeMenu();
        var source = GetSourceText();
        SetDocument(BuildForMode(source));
        Editor.CaretPosition = DocumentCaret.GetPointerAtCharOffset(Editor.Document, Math.Clamp(caret, 0, source.Length));
        if (FindBar.Visibility == Visibility.Visible)
        {
            RecomputeMatches();
            PaintHighlights();
            UpdateMatchLabel();
        }
        SetVerticalScrollOffset(vScroll);
        Editor.Focus();
    }

    private double GetVerticalScrollOffset()
    {
        Editor.ApplyTemplate();
        return Editor.Template?.FindName("PART_ContentHost", Editor) is System.Windows.Controls.ScrollViewer sv
            ? sv.VerticalOffset
            : 0;
    }

    private void SetVerticalScrollOffset(double offset)
    {
        if (offset <= 0) return;
        Editor.ApplyTemplate();
        if (Editor.Template?.FindName("PART_ContentHost", Editor) is System.Windows.Controls.ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset(offset);
        }
    }

    // 阅读模式:右侧滚动条平时隐藏,滚动时出现,5 秒无操作后淡出
    private void InitReadingScrollBar()
    {
        Editor.ApplyTemplate();
        if (Editor.Template?.FindName("PART_ContentHost", Editor) is not System.Windows.Controls.ScrollViewer host) return;
        host.ApplyTemplate();
        if (host.Template?.FindName("PART_VerticalScrollBar", host) is not System.Windows.Controls.Primitives.ScrollBar bar) return;

        _readingScrollBar = bar;
        bar.BeginAnimation(OpacityProperty, null);
        bar.Opacity = 0;
        bar.IsHitTestVisible = false;
        _scrollBarTimer.Stop();
    }

    private void ShowReadingScrollBarTemporarily()
    {
        if (!_reading) return;
        if (_readingScrollBar == null) InitReadingScrollBar();
        if (_readingScrollBar == null) return;

        _readingScrollBar.BeginAnimation(OpacityProperty, null);
        _readingScrollBar.Opacity = 1;
        _readingScrollBar.IsHitTestVisible = true;
        _scrollBarTimer.Stop();
        _scrollBarTimer.Start();
    }

    private void HideReadingScrollBar()
    {
        var bar = _readingScrollBar;
        if (bar == null) return;

        // 鼠标在滚动条上或正在拖动滑块时保持可见
        if (bar.IsMouseOver || IsScrollThumbDragging(bar))
        {
            _scrollBarTimer.Start();
            return;
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) =>
        {
            if (!bar.IsMouseOver && !IsScrollThumbDragging(bar)) bar.IsHitTestVisible = false;
        };
        bar.BeginAnimation(OpacityProperty, fade);
    }

    private static bool IsScrollThumbDragging(System.Windows.Controls.Primitives.ScrollBar bar)
    {
        bar.ApplyTemplate();
        if (bar.Template?.FindName("PART_Track", bar) is System.Windows.Controls.Primitives.Track track && track.Thumb != null)
        {
            return track.Thumb.IsDragging;
        }
        return false;
    }

    private void ResetReadingScrollBar()
    {
        _scrollBarTimer.Stop();
        var bar = _readingScrollBar;
        if (bar == null) return;
        bar.BeginAnimation(OpacityProperty, null);
        bar.Opacity = 1;
        bar.IsHitTestVisible = true;
    }

    // ---- File / export ----

    // 新建:开一个新窗口,便于同时编辑多个文件
    private static int _cascadeIndex;

    private MainWindow CreateNewWindow()
    {
        var w = new MainWindow();
        w.ClearDocument(undoable: false);

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
        CreateNewWindow().Show();
    }

    // 清空:空白或新建时的默认内容直接清空,其余先确认;清空可 Ctrl+Z 撤销
    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (ClearPolicy.NeedsConfirm(GetSourceText(), _welcomeText))
        {
            var result = ConfirmWindow.Ask(this, _theme, "清空文档",
                "确定要清空当前文档吗?清空后可以按 Ctrl+Z 撤销。", "清空", null, "取消");
            if (result != ConfirmResult.Primary) return;
        }
        ClearDocument();
    }

    private void ClearDocument(bool undoable = true)
    {
        _renderTimer.Stop();
        _groupTimer.Stop();

        var before = GetSourceText();
        if (undoable)
        {
            if (before.Length > 0)
            {
                // 清空前的原文入栈,使清空可以 Ctrl+Z 撤回
                int caret = DocumentCaret.GetCaretOffset(Editor.Document, Editor.CaretPosition);
                _undo.Command(before, caret);
            }
        }
        else
        {
            _undo.Reset("", 0);
        }

        SetDocument(BuildForMode(""));
        _savedText = GetSourceText();
        RefreshDirty();

        if (undoable)
        {
            // 记录清空后的空状态,这样清空后再输入时 Ctrl+Z 会先回到空、再回到清空前
            _undo.Change(_savedText, 0);
            _undo.Commit();
        }

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
        LoadFile(dlg.FileName);
    }

    // 从路径加载(供"打开"对话框、右键"打开方式"、命令行共用)
    public void OpenFile(string path) => LoadFile(path);

    private void LoadFile(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            _currentFile = path;
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
            ShowError("打开失败", ex);
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

    // 供更新流程调用:返回 false 表示用户取消(中止更新)
    internal bool ConfirmForUpdate() => ConfirmDiscard();

    internal void SuppressClosingPrompt() => _skipClosingPrompt = true;

    internal string? CurrentFilePath => _currentFile;

    // ---- 设置(齿轮)/ 手动检查更新 ----

    private void OnSettingsMenu(object sender, RoutedEventArgs e)
    {
        var cm = SettingsMenuButton.ContextMenu;
        if (cm == null) return;
        cm.PlacementTarget = SettingsMenuButton;
        cm.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e) => await CheckForUpdatesManuallyAsync();

    private async Task CheckForUpdatesManuallyAsync()
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        ShowUpdateStatus("正在检查更新…", 0);

        try
        {
            var result = await UpdateService.CheckManualAsync();
            var current = UpdateService.CurrentVersion();

            if (result.IsFailure || result.Info == null)
            {
                ShowUpdateStatus("检查更新失败:" + (result.Error ?? "未知错误"), 6000);
                return;
            }

            var info = result.Info;
            if (!UpdateService.IsNewer(info.Version, current))
            {
                ShowUpdateStatus($"已是最新版本 v{current}", 4000);
                return;
            }

            // 手动检查不受「跳过此版本」限制,用户主动要看就给结果
            ShowUpdateStatus($"发现新版本 v{info.Version}", 0);
            var win = new UpdateWindow(info, AppContext.BaseDirectory, current, _theme) { Owner = this };
            win.ShowDialog();

            if (win.SkipRequested)
            {
                var s = SettingsStore.Load(SettingsStore.DefaultDir);
                s.SkippedVersion = info.Version;
                SettingsStore.Save(SettingsStore.DefaultDir, s);
                _settings = s;
            }
        }
        catch (Exception ex)
        {
            ShowUpdateStatus("检查更新失败:" + ex.Message, 6000);
        }
        finally
        {
            _checkingUpdate = false;
            if (UpdateStatusText.Text.StartsWith("正在检查更新", StringComparison.Ordinal))
            {
                HideUpdateStatus();
            }
        }
    }

    private void ShowUpdateStatus(string text, int autoHideMs)
    {
        UpdateStatusText.Text = text;
        UpdateStatusText.Visibility = Visibility.Visible;
        _statusTimer.Stop();
        if (autoHideMs > 0)
        {
            _statusTimer.Interval = TimeSpan.FromMilliseconds(autoHideMs);
            _statusTimer.Start();
        }
    }

    private void HideUpdateStatus()
    {
        _statusTimer.Stop();
        UpdateStatusText.Visibility = Visibility.Collapsed;
    }

    // 有未保存改动时询问;返回 false 表示用户取消(应中止当前操作)
    private bool ConfirmDiscard()
    {
        if (!HasUnsavedChanges()) return true;

        var result = ConfirmWindow.Ask(this, _theme, "未保存的更改",
            "当前文档有未保存的更改,是否保存?", "保存", "不保存", "取消");

        return result switch
        {
            ConfirmResult.Cancel => false,
            ConfirmResult.Primary => Save(),
            _ => true,
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_skipClosingPrompt && !ConfirmDiscard())
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
        if (_reading)
        {
            e.CancelCommand();
            return;
        }

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
        if (_reading)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_reading)
        {
            e.Handled = true;
            return;
        }
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
        if (p1.Parent is FrameworkContentElement fce)
        {
            fce.BringIntoView();
        }
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

        var opts = CurrentSearchOptions();
        var (text, caret) = SearchService.ReplaceOne(
            GetSourceText(), _matches[_matchIndex], ReplaceBox.Text, FindBox.Text, opts);
        ApplyCommandText(text, caret);

        var nextMatches = SearchService.Find(GetSourceText(), FindBox.Text, opts);
        int nextIdx = -1;
        for (int i = 0; i < nextMatches.Count; i++)
        {
            if (nextMatches[i].Start >= caret)
            {
                nextIdx = i;
                break;
            }
        }
        _matchIndex = nextIdx >= 0 ? nextIdx : (nextMatches.Count > 0 ? 0 : -1);
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

        // 阅读模式:仅允许退出与复制/全选,屏蔽编辑类快捷键
        if (_reading)
        {
            if (e.Key == Key.Escape)
            {
                ExitReadingMode();
                e.Handled = true;
                return;
            }
            if (ctrl && e.Key is not (Key.C or Key.A or Key.Insert))
            {
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
            return;
        }

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

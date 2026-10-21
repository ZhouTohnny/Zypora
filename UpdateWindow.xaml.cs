using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Zypora;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly string _appDir;
    private CancellationTokenSource? _cts;
    private bool _updating;

    public UpdateWindow(UpdateInfo info, string appDir, string currentVersion, AppTheme theme = AppTheme.Light)
    {
        InitializeComponent();

        var c = ChromeTheme.Of(theme);
        Resources["Br.WindowBg"] = new SolidColorBrush(c.WindowBg);
        Resources["Br.PanelBg"] = new SolidColorBrush(c.PanelBg);
        Resources["Br.PanelBorder"] = new SolidColorBrush(c.PanelBorder);
        Resources["Br.Fg"] = new SolidColorBrush(c.Fg);
        Resources["Br.Muted"] = new SolidColorBrush(c.Muted);
        Resources["Br.Hover"] = new SolidColorBrush(c.Hover);
        Resources["Br.Accent"] = new SolidColorBrush(c.Checked);
        Resources["Br.AccentFg"] = new SolidColorBrush(c.CheckedFg);

        _info = info;
        _appDir = appDir;

        HeadlineText.Text = $"发现新版本 v{info.Version}(当前 v{currentVersion})";
        var notes = string.IsNullOrWhiteSpace(info.Notes) ? info.Changelog : info.Notes;
        if (string.IsNullOrWhiteSpace(notes))
        {
            NotesBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            NotesText.Text = notes;
            NotesBox.Visibility = Visibility.Visible;
        }
    }

    /// <summary>用户选择了"以后再说"。</summary>
    public bool SkipRequested { get; private set; }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        SkipRequested = true;
        Close();
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (!ConfirmUnsavedChanges()) return;

        PromptPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        _updating = true;
        _cts = new CancellationTokenSource();

        var progress = new Progress<double>(p =>
        {
            Progress.Value = p * 100;
            StatusText.Text = $"正在下载更新… {(int)(p * 100)}%";
        });

        try
        {
            var dir = UpdateService.TempDir(_info.Version);
            var zip = Path.Combine(dir, UpdateService.ZipName);
            var extract = Path.Combine(dir, "extract");

            StatusText.Text = "正在下载更新…";
            if (!await UpdateService.DownloadAsync(_info, zip, progress, _cts.Token))
            {
                Fail("更新失败:无法下载更新包,请稍后重试");
                return;
            }

            StatusText.Text = "正在解压…";
            if (!UpdateService.ExtractUpdate(zip, extract, out var err))
            {
                Fail("更新失败:" + err);
                return;
            }

            StatusText.Text = "正在安装…";
            var apply = UpdateService.ApplyUpdate(_appDir, extract);
            if (!apply.Success)
            {
                Fail("更新失败:" + apply.Error);
                return;
            }

            // 记录本次更新内容,新版本首次启动时提示
            UpdateService.WritePendingInfo(SettingsStore.DefaultDir, _info.Version, _info.ChangelogOrNotes);

            // 更新前已确认过未保存改动,关闭时不再重复弹窗拦截 Shutdown
            foreach (var w in Application.Current.Windows.OfType<MainWindow>())
            {
                w.SuppressClosingPrompt();
            }

            UpdateService.StartNewVersion(_appDir, CurrentOpenFile);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            _updating = false;
            Close();
        }
        catch (Exception ex)
        {
            Fail("更新失败:" + ex.Message);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (_updating) _cts?.Cancel();
        else Close();
    }

    private void Fail(string message)
    {
        _updating = false;
        MessageBox.Show(this, message, "Zypora 更新", MessageBoxButton.OK, MessageBoxImage.Warning);
        ProgressPanel.Visibility = Visibility.Collapsed;
        PromptPanel.Visibility = Visibility.Visible;
    }

    /// <summary>更新会重启程序,先确认所有窗口的未保存改动(任一窗口取消则中止更新)。</summary>
    private bool ConfirmUnsavedChanges()
    {
        var topmost = Topmost;
        Topmost = false;
        try
        {
            foreach (var w in Application.Current.Windows.OfType<MainWindow>())
            {
                if (!w.ConfirmForUpdate()) return false;
            }
            return true;
        }
        finally
        {
            Topmost = topmost;
        }
    }

    private string? CurrentOpenFile
    {
        get
        {
            foreach (var w in Application.Current.Windows.OfType<MainWindow>()) return w.CurrentFilePath;
            return null;
        }
    }
}

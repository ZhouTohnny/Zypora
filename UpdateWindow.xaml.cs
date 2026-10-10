using System.IO;
using System.Windows;

namespace Zypora;

public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly string _appDir;
    private CancellationTokenSource? _cts;
    private bool _updating;

    public UpdateWindow(UpdateInfo info, string appDir, string currentVersion)
    {
        InitializeComponent();
        _info = info;
        _appDir = appDir;

        HeadlineText.Text = $"发现新版本 v{info.Version}(当前 v{currentVersion})";
        if (string.IsNullOrWhiteSpace(info.Notes))
        {
            NotesText.Visibility = Visibility.Collapsed;
        }
        else
        {
            NotesText.Text = info.Notes;
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

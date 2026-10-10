using System.Windows;

namespace Zypora;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var files = StartupArgs.OpenableFiles(e.Args);

        var main = new MainWindow();
        if (files.Count > 0) main.OpenFile(files[0]);
        main.Show();

        for (int i = 1; i < files.Count; i++)
        {
            var w = new MainWindow();
            w.OpenFile(files[i]);
            w.Show();
        }

        // 清理上次更新留下的备份
        UpdateService.CleanupBackup(AppContext.BaseDirectory);

        if (!StartupArgs.HasNoUpdateCheck(e.Args))
        {
            _ = Task.Run(() => CheckForUpdatesAsync(main));
        }
    }

    /// <summary>后台检查更新:任何失败都静默,不影响使用。</summary>
    private async Task CheckForUpdatesAsync(Window owner)
    {
        UpdateInfo? info;
        try
        {
            info = await UpdateService.CheckAsync().ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        if (info == null || owner.Dispatcher.HasShutdownStarted) return;

        try
        {
            owner.Dispatcher.Invoke(() =>
            {
                var current = UpdateService.CurrentVersion();
                if (!UpdateService.IsNewer(info.Version, current)) return;

                var settings = SettingsStore.Load(SettingsStore.DefaultDir);
                if (string.Equals(settings.SkippedVersion, info.Version, StringComparison.OrdinalIgnoreCase)) return;

                var win = new UpdateWindow(info, AppContext.BaseDirectory, current) { Owner = owner };
                win.ShowDialog();

                if (win.SkipRequested)
                {
                    var s = SettingsStore.Load(SettingsStore.DefaultDir);
                    s.SkippedVersion = info.Version;
                    SettingsStore.Save(SettingsStore.DefaultDir, s);
                }
            });
        }
        catch
        {
            // 静默
        }
    }
}

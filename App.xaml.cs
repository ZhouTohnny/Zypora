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

        // 若刚通过程序内更新升级,提示本次更新内容(失败不影响使用)
        try
        {
            var pending = UpdateService.ConsumePendingInfo(SettingsStore.DefaultDir);
            if (pending != null)
            {
                var settings = SettingsStore.Load(SettingsStore.DefaultDir);
                var whatsNew = new WhatsNewWindow(pending, settings.ResolveTheme()) { Owner = main };
                whatsNew.ShowDialog();
            }
        }
        catch
        {
            // 静默
        }

        if (!StartupArgs.HasNoUpdateCheck(e.Args))
        {
            var settings = SettingsStore.Load(SettingsStore.DefaultDir);
            var center = UpdateCenter.Shared;
            center.Ready += info => Dispatcher.BeginInvoke(new Action(() =>
            {
                // 自动更新开启时保持静默;关闭时提示已就绪
                if (SettingsStore.Load(SettingsStore.DefaultDir).AutoUpdate) return;
                foreach (var window in Windows.OfType<MainWindow>())
                {
                    window.ShowUpdateReadyHint(info);
                }
            }));
            _ = center.StartBackground(UpdateService.CurrentVersion(), settings);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (SettingsStore.Load(SettingsStore.DefaultDir).AutoUpdate)
            {
                UpdateCenter.Shared.ApplyReadyAtExit(AppContext.BaseDirectory, SettingsStore.DefaultDir);
            }
        }
        catch
        {
            // 静默:安装失败不影响退出,下次启动仍可用旧版
        }
        base.OnExit(e);
    }
}

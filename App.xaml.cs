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
    }
}

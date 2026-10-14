using System.Windows;
using System.Windows.Media;

namespace Zypora;

// 更新完成后首次启动时的"本次更新了什么"
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow(PendingUpdate info, AppTheme theme)
    {
        var c = ChromeTheme.Of(theme);
        Resources["Br.WindowBg"] = new SolidColorBrush(c.WindowBg);
        Resources["Br.PanelBg"] = new SolidColorBrush(c.PanelBg);
        Resources["Br.PanelBorder"] = new SolidColorBrush(c.PanelBorder);
        Resources["Br.Fg"] = new SolidColorBrush(c.Fg);
        Resources["Br.Muted"] = new SolidColorBrush(c.Muted);
        Resources["Br.Hover"] = new SolidColorBrush(c.Hover);

        InitializeComponent();

        HeadlineText.Text = $"已更新到 v{info.Version}";
        BodyText.Text = string.IsNullOrWhiteSpace(info.Text)
            ? "本次更新已完成,感谢使用 Zypora。"
            : info.Text;

        Loaded += (_, _) => CenterOnOwner();
    }

    // 启动阶段 CenterOwner 可能算错位置(会跑到屏幕左侧),这里手动居中并保证不出屏
    private void CenterOnOwner()
    {
        try
        {
            if (Owner != null && Owner.ActualWidth > 0)
            {
                Left = Owner.Left + (Owner.ActualWidth - ActualWidth) / 2;
                Top = Owner.Top + (Owner.ActualHeight - ActualHeight) / 2;
            }

            var wa = SystemParameters.WorkArea;
            if (Left < wa.Left) Left = wa.Left + 12;
            if (Top < wa.Top) Top = wa.Top + 12;
            if (Left + ActualWidth > wa.Right) Left = wa.Right - ActualWidth - 12;
            if (Top + ActualHeight > wa.Bottom) Top = wa.Bottom - ActualHeight - 12;
        }
        catch
        {
            // 位置失败不影响使用
        }
    }

    private void OnOk(object sender, RoutedEventArgs e) => Close();
}

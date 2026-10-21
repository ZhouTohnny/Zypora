using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Zypora;

public enum ConfirmResult
{
    Primary,
    Secondary,
    Cancel,
}

// 主题化确认弹窗:清空确认 / 退出前保存提示 / 新建或打开前丢弃提示
public partial class ConfirmWindow : Window
{
    private ConfirmResult _result = ConfirmResult.Cancel;

    public ConfirmResult Result => _result;

    public ConfirmWindow(AppTheme theme, string headline, string message,
        string primaryText, string? secondaryText = null, string cancelText = "取消")
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

        HeadlineText.Text = headline;
        MessageText.Text = message;

        AddButton(primaryText, ConfirmResult.Primary, accent: true, isDefault: true, isCancel: false);
        if (secondaryText != null)
        {
            AddButton(secondaryText, ConfirmResult.Secondary, accent: false, isDefault: false, isCancel: false);
        }
        AddButton(cancelText, ConfirmResult.Cancel, accent: false, isDefault: false, isCancel: true);
    }

    public static ConfirmResult Ask(Window owner, AppTheme theme, string headline, string message,
        string primaryText, string? secondaryText = null, string cancelText = "取消")
    {
        var dlg = new ConfirmWindow(theme, headline, message, primaryText, secondaryText, cancelText)
        {
            Owner = owner,
        };
        dlg.ShowDialog();
        return dlg.Result;
    }

    private void AddButton(string text, ConfirmResult result, bool accent, bool isDefault, bool isCancel)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource("DialogButton"),
            Background = (Brush)FindResource(accent ? "Br.Accent" : "Br.Hover"),
            Foreground = (Brush)FindResource(accent ? "Br.AccentFg" : "Br.Fg"),
            IsDefault = isDefault,
            IsCancel = isCancel,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 84,
        };
        button.Click += (_, _) =>
        {
            _result = result;
            Close();
        };
        ButtonPanel.Children.Add(button);
    }
}

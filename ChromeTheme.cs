using System.Windows.Media;

namespace Zypora;

// 界面(窗口/面板/编辑区/菜单)配色:三套主题的纯数据
public sealed record ChromeTheme(
    Color WindowBg, Color PanelBg, Color FindBg, Color PanelBorder,
    Color Fg, Color Muted, Color Hover, Color Checked, Color CheckedFg,
    Color EditorBg, Color Caret,
    Color MenuBg, Color MenuFg, Color MenuHover, Color MenuBorder)
{
    public static ChromeTheme Of(AppTheme theme) => theme switch
    {
        AppTheme.Dark => Dark,
        AppTheme.Eye => Eye,
        _ => Light,
    };

    public static readonly ChromeTheme Light = new(
        WindowBg: Hex("#F4F4F4"), PanelBg: Hex("#FFFFFF"), FindBg: Hex("#FAFAFA"), PanelBorder: Hex("#E0E0E0"),
        Fg: Hex("#1E1E1E"), Muted: Hex("#666666"), Hover: Hex("#E0E0E0"), Checked: Hex("#CFE3FF"), CheckedFg: Hex("#1565C0"),
        EditorBg: Hex("#FFFFFF"), Caret: Hex("#000000"),
        MenuBg: Hex("#FFFFFF"), MenuFg: Hex("#1E1E1E"), MenuHover: Hex("#E0E0E0"), MenuBorder: Hex("#E0E0E0"));

    public static readonly ChromeTheme Dark = new(
        WindowBg: Hex("#1E1E1E"), PanelBg: Hex("#252526"), FindBg: Hex("#2A2A2B"), PanelBorder: Hex("#3A3A3A"),
        Fg: Hex("#E8E8E8"), Muted: Hex("#A0A0A0"), Hover: Hex("#3A3A3A"), Checked: Hex("#2F4A6E"), CheckedFg: Hex("#9CC7FF"),
        EditorBg: Hex("#1E1E1E"), Caret: Hex("#FFFFFF"),
        MenuBg: Hex("#2A2A2B"), MenuFg: Hex("#E8E8E8"), MenuHover: Hex("#3A3A3A"), MenuBorder: Hex("#3A3A3A"));

    // 护眼(豆沙绿)
    public static readonly ChromeTheme Eye = new(
        WindowBg: Hex("#C7EDCC"), PanelBg: Hex("#D6F0D9"), FindBg: Hex("#CDEBD2"), PanelBorder: Hex("#A8CBAE"),
        Fg: Hex("#2E3B2E"), Muted: Hex("#5C6E5C"), Hover: Hex("#BCE3C2"), Checked: Hex("#A9D9B2"), CheckedFg: Hex("#1F5B33"),
        EditorBg: Hex("#C7EDCC"), Caret: Hex("#2E3B2E"),
        MenuBg: Hex("#D6F0D9"), MenuFg: Hex("#2E3B2E"), MenuHover: Hex("#BCE3C2"), MenuBorder: Hex("#A8CBAE"));

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

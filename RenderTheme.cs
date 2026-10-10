using System.Windows.Media;

namespace Zypora;

public enum AppTheme
{
    Light,
    Dark,
    Eye,
}

// 主题代码与显示名(设置持久化 / 菜单项 Tag 共用)
public static class AppThemeCodes
{
    public const string LightCode = "light";
    public const string DarkCode = "dark";
    public const string EyeCode = "eye";

    public static AppTheme Parse(string? code) => (code ?? "").Trim().ToLowerInvariant() switch
    {
        DarkCode => AppTheme.Dark,
        EyeCode => AppTheme.Eye,
        _ => AppTheme.Light,
    };

    public static string ToCode(AppTheme theme) => theme switch
    {
        AppTheme.Dark => DarkCode,
        AppTheme.Eye => EyeCode,
        _ => LightCode,
    };

    public static string Label(AppTheme theme) => theme switch
    {
        AppTheme.Dark => "夜间",
        AppTheme.Eye => "护眼",
        _ => "常规",
    };
}

// 渲染主题:常规 / 夜间 / 护眼 三套调色板
public sealed class RenderTheme
{
    public Brush Text { get; init; } = Brushes.Black;
    public Brush HeadingFg { get; init; } = Brushes.Black;
    public Brush QuoteFg { get; init; } = Brushes.Gray;
    public Brush QuoteLine { get; init; } = Brushes.SteelBlue;
    public Brush RuleLine { get; init; } = Brushes.LightGray;
    public Brush CodeBg { get; init; } = Brushes.WhiteSmoke;
    public Brush CodeFg { get; init; } = Brushes.Firebrick;
    public Brush Faint { get; init; } = Brushes.LightGray;
    public Brush GridLine { get; init; } = Brushes.LightGray;
    public Brush GridLineStrong { get; init; } = Brushes.Gray;
    public Brush PlaceholderBg { get; init; } = Brushes.WhiteSmoke;
    public Brush Bullet { get; init; } = Brushes.Black;

    public static readonly RenderTheme Light = new()
    {
        Text = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28)),
        HeadingFg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
        QuoteFg = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
        QuoteLine = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)),
        RuleLine = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
        CodeBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
        CodeFg = new SolidColorBrush(Color.FromRgb(0xC8, 0x28, 0x28)),
        Faint = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8)),
        GridLine = new SolidColorBrush(Color.FromRgb(0xD5, 0xD5, 0xD5)),
        GridLineStrong = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xA8)),
        PlaceholderBg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
        Bullet = Brushes.Black,
    };

    public static readonly RenderTheme Dark = new()
    {
        Text = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
        HeadingFg = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2)),
        QuoteFg = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)),
        QuoteLine = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)),
        RuleLine = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
        CodeBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
        CodeFg = new SolidColorBrush(Color.FromRgb(0xE0, 0x6C, 0x75)),
        Faint = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x6E)),
        GridLine = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)),
        GridLineStrong = new SolidColorBrush(Color.FromRgb(0x58, 0x58, 0x58)),
        PlaceholderBg = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
        Bullet = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)),
    };

    // 护眼:豆沙绿底、深墨绿字
    public static readonly RenderTheme Eye = new()
    {
        Text = new SolidColorBrush(Color.FromRgb(0x2E, 0x3B, 0x2E)),
        HeadingFg = new SolidColorBrush(Color.FromRgb(0x24, 0x30, 0x24)),
        QuoteFg = new SolidColorBrush(Color.FromRgb(0x4A, 0x5A, 0x4A)),
        QuoteLine = new SolidColorBrush(Color.FromRgb(0x4E, 0x8C, 0x5A)),
        RuleLine = new SolidColorBrush(Color.FromRgb(0xA8, 0xCB, 0xAE)),
        CodeBg = new SolidColorBrush(Color.FromRgb(0xBC, 0xE3, 0xC2)),
        CodeFg = new SolidColorBrush(Color.FromRgb(0xA0, 0x30, 0x30)),
        Faint = new SolidColorBrush(Color.FromRgb(0x8F, 0xAE, 0x95)),
        GridLine = new SolidColorBrush(Color.FromRgb(0xA8, 0xCB, 0xAE)),
        GridLineStrong = new SolidColorBrush(Color.FromRgb(0x7F, 0xA9, 0x88)),
        PlaceholderBg = new SolidColorBrush(Color.FromRgb(0xC2, 0xE5, 0xC8)),
        Bullet = new SolidColorBrush(Color.FromRgb(0x2E, 0x3B, 0x2E)),
    };

    public static RenderTheme Of(AppTheme theme) => theme switch
    {
        AppTheme.Dark => Dark,
        AppTheme.Eye => Eye,
        _ => Light,
    };
}

using System.Windows.Media;

namespace Zypora;

// 渲染主题:浅色 / 深色两套调色板
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
}

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Zypora;

public static class EditableRenderer
{
    private static readonly string Mono = "Consolas";
    private static readonly string Body = "Microsoft YaHei UI";

    private static readonly Brush Faint = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xB8));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x28));
    private static readonly Brush CodeBg = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
    private static readonly Brush CodeFg = new SolidColorBrush(Color.FromRgb(0xC8, 0x28, 0x28));
    private static readonly Brush QuoteLine = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly Brush RuleLine = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
    private static readonly Brush HeadingFg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly Brush QuoteFg = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    private static readonly Regex BoldRegex = new Regex(@"(\*\*|__)(.+?)(\*\*|__)", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new Regex(@"(?<!\*)(\*|_)(?!\*)(.+?)(\*|_)", RegexOptions.Compiled);
    private static readonly Regex CodeRegex = new Regex(@"(`[^`]+`)", RegexOptions.Compiled);

    public static string ReadSource(FlowDocument doc)
    {
        var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        if (text.EndsWith("\r\n"))
        {
            text = text.Substring(0, text.Length - 2);
        }
        return text;
    }

    public static FlowDocument Build(string markdown) => Build(markdown, preview: false);

    public static FlowDocument BuildPreview(string markdown) => Build(markdown, preview: true);

    public static FlowDocument BuildRaw(string markdown)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily(Mono),
            FontSize = 14,
            PagePadding = new Thickness(40, 28, 40, 40),
            Background = Brushes.White,
            Foreground = Text,
        };

        if (string.IsNullOrEmpty(markdown))
        {
            doc.Blocks.Add(new Paragraph());
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            var p = new Paragraph { Margin = new Thickness(0, 0, 0, 0) };
            if (line.Length > 0) p.Inlines.Add(new Run(line));
            doc.Blocks.Add(p);
        }

        return doc;
    }


    private static Run Marker(string text, bool preview) =>
        preview
            ? new Run(text) { Foreground = Brushes.Transparent, FontSize = 1 }
            : new Run(text) { Foreground = Faint };

    private static InlineUIContainer CreateBullet()
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = Brushes.Black,
            Margin = new Thickness(0, 0, 7, 0),
        };
        return new InlineUIContainer(dot) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static FlowDocument Build(string markdown, bool preview)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily(Body),
            FontSize = 16,
            PagePadding = new Thickness(40, 28, 40, 40),
            Background = Brushes.White,
            Foreground = Text,
        };

        if (string.IsNullOrEmpty(markdown))
        {
            doc.Blocks.Add(new Paragraph());
            return doc;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        bool inFence = false;
        string fenceOpen = "";
        var codeLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (inFence)
            {
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    FlushCode(doc, fenceOpen, codeLines, line, preview);
                    inFence = false;
                }
                else
                {
                    codeLines.Add(line);
                }
                continue;
            }

            if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
            {
                inFence = true;
                fenceOpen = line;
                codeLines.Clear();
                continue;
            }

            if (line.Length == 0)
            {
                doc.Blocks.Add(new Paragraph());
                continue;
            }

            doc.Blocks.Add(BuildLine(line, trimmed, preview));
        }

        if (inFence)
        {
            FlushCode(doc, fenceOpen, codeLines, null, preview);
        }

        return doc;
    }

    private static void FlushCode(FlowDocument doc, string openLine, List<string> codeLines, string? closeLine, bool preview)
    {
        // 围栏段落:预览下标记不可见,故用极小字号避免多出一条空行,仅靠内边距撑出代码块上下边缘
        double fenceSize = preview ? 1 : 14;
        doc.Blocks.Add(CodeParagraph(openLine, Marker(openLine, preview), new Thickness(12, preview ? 4 : 8, 12, 0), new Thickness(0, 6, 0, 0), fenceSize));

        for (int i = 0; i < codeLines.Count; i++)
        {
            bool last = i == codeLines.Count - 1 && closeLine == null;
            doc.Blocks.Add(CodeParagraph(
                codeLines[i],
                new Run(codeLines[i]) { Foreground = CodeFg },
                new Thickness(12, 0, 12, last ? (preview ? 4 : 8) : 0),
                new Thickness(0, 0, 0, last && !preview ? 6 : 0),
                14));
        }

        if (closeLine != null)
        {
            doc.Blocks.Add(CodeParagraph(closeLine, Marker(closeLine, preview), new Thickness(12, 0, 12, preview ? 4 : 8), new Thickness(0, 0, 0, 6), fenceSize));
        }
    }

    private static Paragraph CodeParagraph(string _, Inline content, Thickness padding, Thickness margin, double fontSize)
    {
        var p = new Paragraph
        {
            FontFamily = new FontFamily(Mono),
            FontSize = fontSize,
            Background = CodeBg,
            Padding = padding,
            Margin = margin,
        };
        p.Inlines.Add(content);
        return p;
    }

    private static Block BuildLine(string line, string trimmed, bool preview)
    {
        var p = new Paragraph { Margin = new Thickness(0, 3, 0, 3) };

        int leadLen = line.Length - trimmed.Length;
        if (leadLen > 0)
        {
            p.Inlines.Add(new Run(line.Substring(0, leadLen)));
        }

        // Heading
        int hash = 0;
        while (hash < trimmed.Length && trimmed[hash] == '#') hash++;
        if (hash > 0 && hash <= 6 && (hash >= trimmed.Length || trimmed[hash] == ' '))
        {
            double size = hash switch { 1 => 30, 2 => 24, 3 => 20, 4 => 18, _ => 16 };
            int markerLen = hash < trimmed.Length ? hash + 1 : hash;
            var marker = trimmed.Substring(0, markerLen);
            var content = trimmed.Substring(markerLen);
            p.FontSize = size;
            p.FontWeight = FontWeights.SemiBold;
            p.Foreground = HeadingFg;
            p.Inlines.Add(Marker(marker, preview));
            AppendInlineMarkdown(p, content, preview);
            return p;
        }

        // Quote
        if (trimmed.StartsWith(">"))
        {
            p.BorderBrush = QuoteLine;
            p.BorderThickness = new Thickness(3, 0, 0, 0);
            p.Padding = new Thickness(12, 4, 0, 4);
            p.Foreground = QuoteFg;
            p.Inlines.Add(Marker(">", preview));
            AppendInlineMarkdown(p, trimmed.Substring(1), preview);
            return p;
        }

        // Unordered list:实心小黑圆点
        var ul = Regex.Match(trimmed, @"^[-*+]\s+");
        if (ul.Success)
        {
            var marker = trimmed.Substring(0, ul.Groups[0].Value.Length);
            // InlineUIContainer 在 TextRange 中占 1 个空格,故隐藏标记少渲染最后一个空格,
            // 使文本总长与源码完全一致,保证光标映射与保存不损坏源码。
            p.Inlines.Add(new Run(marker.Substring(0, marker.Length - 1)) { Foreground = Brushes.Transparent, FontSize = 1 });
            p.Inlines.Add(CreateBullet());
            AppendInlineMarkdown(p, trimmed.Substring(marker.Length), preview);
            return p;
        }

        // Ordered list
        var ol = Regex.Match(trimmed, @"^\d+[.)]\s+");
        if (ol.Success)
        {
            var marker = trimmed.Substring(0, ol.Groups[0].Value.Length);
            p.Inlines.Add(preview ? new Run(marker) { Foreground = QuoteLine } : Marker(marker, false));
            AppendInlineMarkdown(p, trimmed.Substring(marker.Length), preview);
            return p;
        }

        // Horizontal rule
        if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
        {
            p.Margin = new Thickness(0, 10, 0, 10);
            p.Inlines.Add(Marker(trimmed, preview));
            if (preview)
            {
                p.BorderBrush = RuleLine;
                p.BorderThickness = new Thickness(0, 0, 0, 1);
            }
            return p;
        }

        // Normal paragraph
        AppendInlineMarkdown(p, trimmed, preview);
        return p;
    }

    private static void AppendInlineMarkdown(Paragraph p, string text, bool preview)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            var bold = BoldRegex.Match(text, pos);
            var code = CodeRegex.Match(text, pos);
            var italic = ItalicRegex.Match(text, pos);

            Match? next = null;
            if (bold.Success && (next == null || bold.Index < next.Index)) next = bold;
            if (code.Success && (next == null || code.Index < next.Index)) next = code;
            if (italic.Success && (next == null || italic.Index < next.Index)) next = italic;

            if (next == null || next.Index < 0)
            {
                p.Inlines.Add(new Run(text.Substring(pos)));
                break;
            }

            if (next.Index > pos)
            {
                p.Inlines.Add(new Run(text.Substring(pos, next.Index - pos)));
            }

            if (next == bold)
            {
                p.Inlines.Add(Marker(bold.Groups[1].Value, preview));
                p.Inlines.Add(new Run(bold.Groups[2].Value) { FontWeight = FontWeights.Bold });
                p.Inlines.Add(Marker(bold.Groups[3].Value, preview));
            }
            else if (next == code)
            {
                var content = code.Groups[1].Value.Trim('`');
                p.Inlines.Add(Marker("`", preview));
                p.Inlines.Add(new Run(content) { FontFamily = new FontFamily(Mono), Background = CodeBg, Foreground = CodeFg });
                p.Inlines.Add(Marker("`", preview));
            }
            else // italic
            {
                p.Inlines.Add(Marker(italic.Groups[1].Value, preview));
                p.Inlines.Add(new Run(italic.Groups[2].Value) { FontStyle = FontStyles.Italic });
                p.Inlines.Add(Marker(italic.Groups[3].Value, preview));
            }

            pos = next.Index + next.Length;
        }
    }
}

using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zypora;

public static class EditableRenderer
{
    private static readonly string Mono = "Consolas";
    private static readonly string Body = "Microsoft YaHei UI";

    private static readonly Regex BoldRegex = new(@"(\*\*|__)(.+?)(\*\*|__)", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(?<!\*)(\*|_)(?!\*)(.+?)(\*|_)", RegexOptions.Compiled);
    private static readonly Regex CodeRegex = new(@"(`[^`]+`)", RegexOptions.Compiled);

    public static string ReadSource(FlowDocument doc)
    {
        var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        if (text.EndsWith("\r\n"))
        {
            text = text.Substring(0, text.Length - 2);
        }
        return text;
    }

    public static FlowDocument Build(string markdown) => Build(markdown, preview: false, docDir: null, appDir: null, RenderTheme.Light);

    public static FlowDocument BuildPreview(string markdown, string? docDir = null, string? appDir = null, RenderTheme? theme = null)
        => Build(markdown, preview: true, docDir, appDir, theme ?? RenderTheme.Light);

    public static FlowDocument BuildRaw(string markdown, RenderTheme? theme = null)
    {
        var t = theme ?? RenderTheme.Light;
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily(Mono),
            FontSize = 14,
            PagePadding = new Thickness(40, 28, 40, 40),
            Foreground = t.Text,
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

    private static Run Marker(string text, bool preview, RenderTheme t) =>
        preview
            ? new Run(text) { Foreground = Brushes.Transparent, FontSize = 1 }
            : new Run(text) { Foreground = t.Faint };

    private static InlineUIContainer CreateBullet(RenderTheme t)
    {
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = t.Bullet,
            Margin = new Thickness(0, 0, 7, 0),
        };
        return new InlineUIContainer(dot) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static FlowDocument Build(string markdown, bool preview, string? docDir, string? appDir, RenderTheme theme)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily(Body),
            FontSize = 16,
            PagePadding = new Thickness(40, 28, 40, 40),
            Foreground = theme.Text,
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

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (inFence)
            {
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    FlushCode(doc, fenceOpen, codeLines, line, preview, theme);
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

            if (preview && TableParser.TryParse(lines, i, out var table, out var tableEnd))
            {
                AppendTable(doc, lines, i, tableEnd, table, preview, docDir, appDir, theme);
                i = tableEnd - 1;
                continue;
            }

            if (line.Length == 0)
            {
                doc.Blocks.Add(new Paragraph());
                continue;
            }

            doc.Blocks.Add(BuildLine(line, trimmed, preview, docDir, appDir, theme));
        }

        if (inFence)
        {
            FlushCode(doc, fenceOpen, codeLines, null, preview, theme);
        }

        return doc;
    }

    private static void FlushCode(FlowDocument doc, string openLine, List<string> codeLines, string? closeLine, bool preview, RenderTheme t)
    {
        double fenceSize = preview ? 1 : 14;
        doc.Blocks.Add(CodeParagraph(openLine, Marker(openLine, preview, t), new Thickness(12, preview ? 4 : 8, 12, 0), new Thickness(0, 6, 0, 0), fenceSize, t));

        for (int i = 0; i < codeLines.Count; i++)
        {
            bool last = i == codeLines.Count - 1 && closeLine == null;
            doc.Blocks.Add(CodeParagraph(
                codeLines[i],
                new Run(codeLines[i]) { Foreground = t.CodeFg },
                new Thickness(12, 0, 12, last ? (preview ? 4 : 8) : 0),
                new Thickness(0, 0, 0, last && !preview ? 6 : 0),
                14,
                t));
        }

        if (closeLine != null)
        {
            doc.Blocks.Add(CodeParagraph(closeLine, Marker(closeLine, preview, t), new Thickness(12, 0, 12, preview ? 4 : 8), new Thickness(0, 0, 0, 6), fenceSize, t));
        }
    }

    private static Paragraph CodeParagraph(string _, Inline content, Thickness padding, Thickness margin, double fontSize, RenderTheme t)
    {
        var p = new Paragraph
        {
            FontFamily = new FontFamily(Mono),
            FontSize = fontSize,
            Background = t.CodeBg,
            Padding = padding,
            Margin = margin,
        };
        p.Inlines.Add(content);
        return p;
    }

    private static Block BuildLine(string line, string trimmed, bool preview, string? docDir, string? appDir, RenderTheme t)
    {
        // 图片行(整行是 ![alt](src) 且含空格):隐藏文本 + 容器保持逐字恒等
        if (preview && line.Contains(' ') && ImageSupport.TryParseImageLine(trimmed, out _, out var imgSrc))
        {
            int lastSpace = line.LastIndexOf(' ');
            var ip = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
            ip.Inlines.Add(new Run(line.Substring(0, lastSpace)) { Foreground = Brushes.Transparent, FontSize = 1 });
            ip.Inlines.Add(new InlineUIContainer(BuildImageVisual(imgSrc, docDir, appDir, t)) { BaselineAlignment = BaselineAlignment.Center });
            ip.Inlines.Add(new Run(line.Substring(lastSpace + 1)) { Foreground = Brushes.Transparent, FontSize = 1 });
            return ip;
        }

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
            p.Foreground = t.HeadingFg;
            p.Inlines.Add(Marker(marker, preview, t));
            AppendInlineMarkdown(p, content, preview, t);
            return p;
        }

        // Quote
        if (trimmed.StartsWith(">"))
        {
            p.BorderBrush = t.QuoteLine;
            p.BorderThickness = new Thickness(3, 0, 0, 0);
            p.Padding = new Thickness(12, 4, 0, 4);
            p.Foreground = t.QuoteFg;
            p.Inlines.Add(Marker(">", preview, t));
            AppendInlineMarkdown(p, trimmed.Substring(1), preview, t);
            return p;
        }

        // Unordered list:实心圆点
        var ul = Regex.Match(trimmed, @"^[-*+]\s+");
        if (ul.Success)
        {
            var marker = trimmed.Substring(0, ul.Groups[0].Value.Length);
            p.Inlines.Add(new Run(marker.Substring(0, marker.Length - 1)) { Foreground = Brushes.Transparent, FontSize = 1 });
            p.Inlines.Add(CreateBullet(t));
            AppendInlineMarkdown(p, trimmed.Substring(marker.Length), preview, t);
            return p;
        }

        // Ordered list
        var ol = Regex.Match(trimmed, @"^\d+[.)]\s+");
        if (ol.Success)
        {
            var marker = trimmed.Substring(0, ol.Groups[0].Value.Length);
            p.Inlines.Add(preview ? new Run(marker) { Foreground = t.QuoteLine } : Marker(marker, false, t));
            AppendInlineMarkdown(p, trimmed.Substring(marker.Length), preview, t);
            return p;
        }

        // Horizontal rule
        if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
        {
            p.Margin = new Thickness(0, 10, 0, 10);
            p.Inlines.Add(Marker(trimmed, preview, t));
            if (preview)
            {
                p.BorderBrush = t.RuleLine;
                p.BorderThickness = new Thickness(0, 0, 0, 1);
            }
            return p;
        }

        // Normal paragraph
        AppendInlineMarkdown(p, trimmed, preview, t);
        return p;
    }

    private static void AppendInlineMarkdown(Paragraph p, string text, bool preview, RenderTheme t)
    {
        foreach (var inline in ParseInlines(text, preview, t))
        {
            p.Inlines.Add(inline);
        }
    }

    private static List<Inline> ParseInlines(string text, bool preview, RenderTheme t)
    {
        var result = new List<Inline>();
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
                result.Add(new Run(text.Substring(pos)));
                break;
            }

            if (next.Index > pos)
            {
                result.Add(new Run(text.Substring(pos, next.Index - pos)));
            }

            if (next == bold)
            {
                result.Add(Marker(bold.Groups[1].Value, preview, t));
                result.Add(new Run(bold.Groups[2].Value) { FontWeight = FontWeights.Bold });
                result.Add(Marker(bold.Groups[3].Value, preview, t));
            }
            else if (next == code)
            {
                var content = code.Groups[1].Value.Trim('`');
                result.Add(Marker("`", preview, t));
                result.Add(new Run(content) { FontFamily = new FontFamily(Mono), Background = t.CodeBg, Foreground = t.CodeFg });
                result.Add(Marker("`", preview, t));
            }
            else // italic
            {
                result.Add(Marker(italic.Groups[1].Value, preview, t));
                result.Add(new Run(italic.Groups[2].Value) { FontStyle = FontStyles.Italic });
                result.Add(Marker(italic.Groups[3].Value, preview, t));
            }

            pos = next.Index + next.Length;
        }
        return result;
    }

    // ---- 表格 ----

    private static void AppendTable(FlowDocument doc, string[] lines, int start, int end, TableModel model, bool preview, string? docDir, string? appDir, RenderTheme t)
    {
        int cols = model.ColumnCount;
        var widths = new double[cols];
        for (int c = 0; c < cols; c++)
        {
            int max = 0;
            foreach (var row in model.Rows)
            {
                if (c < row.Cells.Count) max = Math.Max(max, DisplayWidth(row.Cells[c].Text));
            }
            widths[c] = Math.Max(48, max * 9 + 18);
        }

        for (int k = start; k < end; k++)
        {
            var line = lines[k];

            if (TableParser.IsSeparatorLine(line))
            {
                var sep = new Paragraph { Margin = new Thickness(0) };
                sep.Inlines.Add(new Run(line) { Foreground = Brushes.Transparent, FontSize = 1 });
                doc.Blocks.Add(sep);
                continue;
            }

            int lastSpace = line.LastIndexOf(' ');
            if (lastSpace < 0)
            {
                doc.Blocks.Add(BuildLine(line, line.TrimStart(), preview, docDir, appDir, t));
                continue;
            }

            bool header = k == start;
            var cells = TableParser.SplitCells(line);
            var visual = BuildRowVisual(model, cells, widths, header, t);

            var p = new Paragraph { Margin = new Thickness(0) };
            p.Inlines.Add(new Run(line.Substring(0, lastSpace)) { Foreground = Brushes.Transparent, FontSize = 1 });
            p.Inlines.Add(new InlineUIContainer(visual) { BaselineAlignment = BaselineAlignment.Top });
            p.Inlines.Add(new Run(line.Substring(lastSpace + 1)) { Foreground = Brushes.Transparent, FontSize = 1 });
            doc.Blocks.Add(p);
        }
    }

    private static FrameworkElement BuildRowVisual(TableModel model, IReadOnlyList<string> cells, double[] widths, bool header, RenderTheme t)
    {
        var grid = new Grid();
        for (int c = 0; c < widths.Length; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[c]) });
        }

        for (int c = 0; c < widths.Length; c++)
        {
            var text = c < cells.Count ? cells[c] : "";
            var tb = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = AlignToTextAlignment(model.Align[c]),
                FontWeight = header ? FontWeights.Bold : FontWeights.Normal,
                FontSize = 15,
                Foreground = t.Text,
            };
            foreach (var inline in ParseInlines(text, preview: true, t))
            {
                tb.Inlines.Add(inline);
            }

            var border = new Border
            {
                BorderBrush = header ? t.GridLineStrong : t.GridLine,
                BorderThickness = header ? new Thickness(0, 0, 1, 2) : new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 4, 8, 4),
                Child = tb,
            };
            Grid.SetColumn(border, c);
            grid.Children.Add(border);
        }

        return new Border
        {
            BorderBrush = t.GridLine,
            BorderThickness = new Thickness(1, 1, 0, 0),
            Child = grid,
        };
    }

    private static FrameworkElement BuildImageVisual(string src, string? docDir, string? appDir, RenderTheme t)
    {
        var resolved = ImageSupport.ResolveImagePath(src, docDir, appDir);
        if (!ImageSupport.IsRemote(resolved) && File.Exists(resolved))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(resolved);
                bmp.EndInit();
                return new Image
                {
                    Source = bmp,
                    MaxWidth = 640,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                };
            }
            catch
            {
                // 加载失败 → 占位
            }
        }

        return new Border
        {
            Background = t.PlaceholderBg,
            BorderBrush = t.GridLine,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            Child = new TextBlock
            {
                Text = src,
                Foreground = t.QuoteFg,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600,
            },
        };
    }

    private static TextAlignment AlignToTextAlignment(ColumnAlign align) => align switch
    {
        ColumnAlign.Center => TextAlignment.Center,
        ColumnAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    private static int DisplayWidth(string s)
    {
        int w = 0;
        foreach (var ch in s) w += ch > 0x2E7F ? 2 : 1;
        return w;
    }
}

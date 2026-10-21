using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Zypora;

public static class PdfExporter
{
    public static byte[] Export(string markdown)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        if (string.IsNullOrWhiteSpace(markdown))
        {
            markdown = "";
        }

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Microsoft YaHei"));

                page.Content().Column(col =>
                {
                    col.Item().Element(block =>
                    {
                        RenderMarkdownToPdf(block, markdown);
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UsePipeTables().Build();

    private static void RenderMarkdownToPdf(IContainer container, string markdown)
    {
        var parsed = Markdig.Markdown.Parse(markdown, Pipeline);

        container.Column(col =>
        {
            foreach (var block in parsed)
            {
                RenderPdfBlock(col.Item(), block);
            }
        });
    }

    private static void RenderPdfBlock(IContainer item, Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var size = heading.Level switch
                {
                    1 => 24f,
                    2 => 20f,
                    3 => 17f,
                    4 => 15f,
                    _ => 13f,
                };
                item.PaddingTop(10).PaddingBottom(4)
                    .Text(RenderInlinesToText(heading.Inline))
                    .FontSize(size).Bold().FontColor(Colors.Black);
                break;

            case ParagraphBlock paragraph:
                item.PaddingVertical(2)
                    .Text(RenderInlinesToText(paragraph.Inline)).FontSize(11);
                break;

            case ListBlock list:
                RenderListToPdf(item, list);
                break;

            case CodeBlock code:
                var ctext = code.Lines.ToString().TrimEnd('\n');
                item.PaddingVertical(4).Background(Colors.Grey.Lighten3)
                    .Padding(8)
                    .Text(ctext).FontFamily("Consolas").FontSize(9);
                break;

            case QuoteBlock quote:
                item.PaddingVertical(3)
                    .BorderLeft(3).BorderColor(Colors.Blue.Medium)
                    .PaddingLeft(8)
                    .Column(col =>
                    {
                        foreach (var child in quote)
                        {
                            if (child is ParagraphBlock qp)
                                col.Item().PaddingVertical(2).Text(RenderInlinesToText(qp.Inline)).FontSize(11).Italic();
                            else
                                RenderPdfBlock(col.Item(), child);
                        }
                    });
                break;

            case ThematicBreakBlock:
                item.PaddingVertical(6).LineHorizontal(1);
                break;

            case Markdig.Extensions.Tables.Table mdTable:
                RenderTableToPdf(item, mdTable);
                break;
        }
    }

    private static void RenderTableToPdf(IContainer container, Markdig.Extensions.Tables.Table mdTable)
    {
        var rows = mdTable.OfType<Markdig.Extensions.Tables.TableRow>().ToList();
        if (rows.Count == 0) return;
        int colCount = Math.Max(1, rows.Max(r => r.Count));

        container.PaddingVertical(4).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                for (int i = 0; i < colCount; i++) cols.RelativeColumn();
            });

            foreach (var row in rows)
            {
                bool isHeader = row.IsHeader;
                for (int c = 0; c < colCount; c++)
                {
                    var cellText = "";
                    if (c < row.Count && row[c] is Markdig.Extensions.Tables.TableCell cell)
                    {
                        var parts = new List<string>();
                        foreach (var b in cell)
                        {
                            if (b is ParagraphBlock pb) parts.Add(RenderInlinesToText(pb.Inline));
                        }
                        cellText = string.Join("\n", parts);
                    }

                    var cellBox = table.Cell()
                        .Border(0.5f)
                        .BorderColor(Colors.Grey.Lighten1)
                        .Background(isHeader ? Colors.Grey.Lighten4 : Colors.White)
                        .PaddingHorizontal(6)
                        .PaddingVertical(4);

                    var textSpan = cellBox.Text(cellText).FontSize(10);
                    if (isHeader) textSpan.Bold();
                }
            }
        });
    }

    private static void RenderListToPdf(IContainer container, ListBlock list)
    {
        container.Column(col =>
        {
            int index = 1;
            foreach (var item in list)
            {
                if (item is not ListItemBlock listItem) continue;
                int number = index;
                col.Item().Row(row =>
                {
                    row.AutoItem().PaddingRight(6)
                        .Text(list.IsOrdered ? $"{number}." : "•").FontSize(11);
                    row.RelativeItem().Column(inner =>
                    {
                        foreach (var child in listItem)
                        {
                            if (child is ParagraphBlock p)
                                inner.Item().Text(RenderInlinesToText(p.Inline)).FontSize(11);
                            else if (child is ListBlock nested)
                                inner.Item().PaddingLeft(14).Element(e => RenderListToPdf(e, nested));
                            else
                                RenderPdfBlock(inner.Item(), child);
                        }
                    });
                });
                index++;
            }
        });
    }

    private static string RenderInlinesToText(ContainerInline? container)
    {
        if (container == null) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            AppendInlineText(sb, inline);
        }
        return sb.ToString();
    }

    private static void AppendInlineText(System.Text.StringBuilder sb, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                sb.Append(literal.Content.ToString());
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case LineBreakInline:
                sb.Append('\n');
                break;
            default:
                if (inline is ContainerInline ci)
                {
                    foreach (var child in ci)
                    {
                        AppendInlineText(sb, child);
                    }
                }
                break;
        }
    }
}

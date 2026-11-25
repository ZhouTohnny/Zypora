using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Zypora;

public sealed record TableCellInfo(
    TableModel Model,
    int RowIndex,
    int ColumnIndex,
    string RawText,
    Border CellBorder,
    IReadOnlyList<TableCellInfo> TableCells);

public static class TableClipboard
{
    private static readonly Regex CodeRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"(\*\*|__)(.+?)(\1)", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(?<![*_])(\*|_)(?![*_])(.+?)(?<![*_])(\1)(?![*_])", RegexOptions.Compiled);

    /// <summary>成功写入剪贴板后触发(参数为简短提示文案,如"已复制单元格")。</summary>
    public static event Action<string>? Copied;

    public static string CellText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text.Trim();
        s = CodeRegex.Replace(s, "$1");
        s = BoldRegex.Replace(s, "$2");
        s = ItalicRegex.Replace(s, "$2");
        return s.Trim();
    }

    public static string RowText(TableModel model, int rowIndex)
    {
        if (model == null || rowIndex < 0 || rowIndex >= model.Rows.Count) return "";
        return RangeText(model, rowIndex, rowIndex, 0, model.ColumnCount - 1);
    }

    public static string RangeText(TableModel model, int r1, int r2, int c1, int c2)
    {
        if (model == null || model.Rows.Count == 0 || model.ColumnCount <= 0) return "";

        int minR = Math.Clamp(Math.Min(r1, r2), 0, model.Rows.Count - 1);
        int maxR = Math.Clamp(Math.Max(r1, r2), 0, model.Rows.Count - 1);
        int minC = Math.Clamp(Math.Min(c1, c2), 0, model.ColumnCount - 1);
        int maxC = Math.Clamp(Math.Max(c1, c2), 0, model.ColumnCount - 1);

        if (minR == maxR && minC == maxC)
        {
            var singleRow = model.Rows[minR];
            return minC < singleRow.Cells.Count ? CellText(singleRow.Cells[minC].Text) : "";
        }

        var sb = new StringBuilder();
        for (int r = minR; r <= maxR; r++)
        {
            if (r > minR) sb.Append("\r\n");
            var row = model.Rows[r];
            for (int c = minC; c <= maxC; c++)
            {
                if (c > minC) sb.Append('\t');
                sb.Append(c < row.Cells.Count ? CellText(row.Cells[c].Text) : "");
            }
        }
        return sb.ToString();
    }

    public static string TableText(TableModel model)
    {
        if (model == null || model.Rows.Count == 0) return "";
        return RangeText(model, 0, model.Rows.Count - 1, 0, model.ColumnCount - 1);
    }

    /// <summary>如果选区文本中包含 Markdown 表格行,将其转换为干净的 TSV 文本。</summary>
    public static string ConvertSelectionText(string selectedText)
    {
        if (string.IsNullOrEmpty(selectedText) || !selectedText.Contains('|')) return selectedText;

        var lines = selectedText.Replace("\r\n", "\n").Split('\n');
        var outLines = new List<string>(lines.Length);
        bool convertedAny = false;

        int i = 0;
        while (i < lines.Length)
        {
            if (TableParser.TryParse(lines, i, out var model, out var end))
            {
                outLines.Add(TableText(model));
                convertedAny = true;
                i = end;
                continue;
            }

            if (TableParser.IsSeparatorLine(lines[i]))
            {
                convertedAny = true;
                i++;
                continue;
            }

            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('|') && trimmed.EndsWith('|') && trimmed.Length > 2)
            {
                var cells = TableParser.SplitCells(lines[i]);
                if (cells.Count > 0)
                {
                    outLines.Add(string.Join("\t", cells.Select(CellText)));
                    convertedAny = true;
                    i++;
                    continue;
                }
            }

            outLines.Add(lines[i]);
            i++;
        }

        return convertedAny ? string.Join("\r\n", outLines) : selectedText;
    }

    public static bool CopyWithNotice(string text, string notice)
    {
        if (string.IsNullOrEmpty(text)) return false;

        bool ok = false;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                ok = true;
                break;
            }
            catch
            {
                try
                {
                    Clipboard.SetText(text);
                    ok = true;
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        if (ok && !string.IsNullOrEmpty(notice))
        {
            try { Copied?.Invoke(notice); } catch { }
        }
        return ok;
    }
}

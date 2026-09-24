using System.Text;
using System.Text.RegularExpressions;

namespace Zypora;

public enum ColumnAlign { None, Left, Center, Right }

public sealed record TableCell(string Text);

public sealed record TableRow(IReadOnlyList<TableCell> Cells, string RawLine);

public sealed record TableModel(IReadOnlyList<TableRow> Rows, int ColumnCount, IReadOnlyList<ColumnAlign> Align);

public static class TableParser
{
    private static readonly Regex SeparatorRegex =
        new(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);

    public static bool IsSeparatorLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        if (!line.Contains('|') || !line.Contains('-')) return false;
        return SeparatorRegex.IsMatch(line);
    }

    public static IReadOnlyList<string> SplitCells(string line)
    {
        var body = line.Trim();
        if (body.StartsWith("|")) body = body.Substring(1);
        if (body.EndsWith("|")) body = body.Substring(0, body.Length - 1);

        var parts = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (c == '\\' && i + 1 < body.Length && body[i + 1] == '|')
            {
                sb.Append('|');
                i++;
                continue;
            }
            if (c == '|')
            {
                parts.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(c);
        }
        parts.Add(sb.ToString().Trim());
        return parts;
    }

    public static bool TryParse(IReadOnlyList<string> lines, int start, out TableModel model, out int end)
    {
        model = null!;
        end = start;

        if (lines == null || start < 0 || start + 1 >= lines.Count) return false;
        if (!lines[start].Contains('|')) return false;
        if (!IsSeparatorLine(lines[start + 1])) return false;

        var rows = new List<TableRow> { MakeRow(lines[start]) };
        int i = start + 2;
        while (i < lines.Count && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i]))
        {
            rows.Add(MakeRow(lines[i]));
            i++;
        }
        end = i;

        var alignCells = SplitCells(lines[start + 1]);
        int cols = Math.Max(rows.Max(r => r.Cells.Count), alignCells.Count);

        var padded = rows.Select(r => new TableRow(Pad(r.Cells, cols), r.RawLine)).ToList();

        var align = new ColumnAlign[cols];
        for (int c = 0; c < cols; c++)
        {
            var cell = c < alignCells.Count ? alignCells[c] : "";
            bool left = cell.StartsWith(":");
            bool right = cell.EndsWith(":");
            align[c] = left && right ? ColumnAlign.Center
                     : left ? ColumnAlign.Left
                     : right ? ColumnAlign.Right
                     : ColumnAlign.None;
        }

        model = new TableModel(padded, cols, align);
        return true;
    }

    private static TableRow MakeRow(string line)
        => new(SplitCells(line).Select(t => new TableCell(t)).ToList(), line);

    private static IReadOnlyList<TableCell> Pad(IReadOnlyList<TableCell> cells, int cols)
    {
        if (cells.Count == cols) return cells;
        var list = new List<TableCell>(cells);
        while (list.Count < cols) list.Add(new TableCell(""));
        return list;
    }

    // 生成并插入一个表格模板,返回新文本与首个表头单元格的选区
    public static (string Text, int SelStart, int SelEnd) InsertTemplate(string source, int caret, int cols = 3, int rows = 2)
    {
        if (caret < 0) caret = 0;
        if (caret > source.Length) caret = source.Length;
        int lineStart = caret == 0 ? 0 : source.LastIndexOf('\n', caret - 1) + 1;

        string newline = source.Contains("\r\n") ? "\r\n" : (source.Contains("\n") ? "\n" : "\r\n");

        var headers = Enumerable.Range(1, cols).Select(i => $"列 {i}").ToArray();
        var lines = new List<string> { Row(headers), SeparatorLine(cols) };
        for (int r = 0; r < rows; r++) lines.Add(Row(new string[cols]));
        var block = string.Join(newline, lines);

        var text = source.Insert(lineStart, block + newline);
        int selStart = lineStart + 2;
        int selEnd = selStart + headers[0].Length;
        return (text, selStart, selEnd);
    }

    private static string Row(IEnumerable<string> cells) => "| " + string.Join(" | ", cells) + " |";

    private static string SeparatorLine(int cols) => "|" + string.Join("|", Enumerable.Repeat("---", cols)) + "|";
}

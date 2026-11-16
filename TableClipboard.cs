using System.Text;

namespace Zypora;

public static class TableClipboard
{
    public static string CellText(string text) => (text ?? "").Trim();

    public static string TableText(TableModel model)
    {
        if (model == null || model.Rows.Count == 0) return "";

        var sb = new StringBuilder();
        for (int r = 0; r < model.Rows.Count; r++)
        {
            if (r > 0) sb.Append("\r\n");
            var row = model.Rows[r];
            for (int c = 0; c < model.ColumnCount; c++)
            {
                if (c > 0) sb.Append('\t');
                sb.Append(c < row.Cells.Count ? CellText(row.Cells[c].Text) : "");
            }
        }
        return sb.ToString();
    }
}

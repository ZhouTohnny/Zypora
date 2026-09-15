using System.Windows.Documents;

namespace Zypora;

public static class DocumentCaret
{
    public static int GetCaretOffset(FlowDocument doc, TextPointer caret)
    {
        return new TextRange(doc.ContentStart, caret).Text.Length;
    }

    public static TextPointer GetPointerAtCharOffset(FlowDocument doc, int offset)
    {
        var nav = doc.ContentStart;
        int consumed = 0;

        while (nav.CompareTo(doc.ContentEnd) < 0)
        {
            if (offset == consumed)
            {
                return nav;
            }

            var next = nav.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next == null) break;

            int len = new TextRange(nav, next).Text.Length;
            if (len == 0)
            {
                nav = next;
                continue;
            }

            if (consumed + len > offset)
            {
                return nav;
            }

            consumed += len;
            nav = next;
        }

        return nav;
    }
}

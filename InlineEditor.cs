namespace Zypora;

public static class InlineEditor
{
    public static (string Text, int Start, int End) Toggle(string source, int start, int end, string marker)
    {
        if (start > end) (start, end) = (end, start);
        if (start == end) return (source, start, end);

        int ml = marker.Length;
        bool single = ml == 1;
        bool inRange = end <= source.Length;

        bool beforeOpen = inRange && start - ml >= 0
            && source.Substring(start - ml, ml) == marker
            && (!single || start - ml - 1 < 0 || source[start - ml - 1] != marker[0]);
        bool afterClose = inRange && end + ml <= source.Length
            && source.Substring(end, ml) == marker
            && (!single || end + ml >= source.Length || source[end + ml] != marker[0]);

        bool insideOpen = inRange && end - start >= 2 * ml
            && source.Substring(start, ml) == marker
            && (!single || start + ml >= source.Length || source[start + ml] != marker[0]);
        bool insideClose = inRange && end - start >= 2 * ml
            && source.Substring(end - ml, ml) == marker
            && (!single || end - ml - 1 < 0 || source[end - ml - 1] != marker[0]);

        if (beforeOpen && afterClose)
        {
            var text = source.Remove(end, ml).Remove(start - ml, ml);
            return (text, start - ml, end - ml);
        }

        if (insideOpen && insideClose)
        {
            var text = source.Remove(end - ml, ml).Remove(start, ml);
            return (text, start, end - 2 * ml);
        }

        var wrapped = source.Insert(end, marker).Insert(start, marker);
        return (wrapped, start + ml, end + ml);
    }

    // 空选区:在光标处插入成对标记(光标居中);若两侧已是标记则取消
    public static (string Text, int Caret) ToggleAtCaret(string source, int caret, string marker)
    {
        int ml = marker.Length;
        bool before = caret - ml >= 0 && source.Substring(caret - ml, ml) == marker;
        bool after = caret + ml <= source.Length && source.Substring(caret, ml) == marker;

        if (before && after)
        {
            var text = source.Remove(caret, ml).Remove(caret - ml, ml);
            return (text, caret - ml);
        }

        var inserted = source.Insert(caret, marker + marker);
        return (inserted, caret + ml);
    }

    // 多行选区包裹为围栏代码块(扩展到整行)
    public static (string Text, int Start, int End) WrapFence(string source, int start, int end)
    {
        if (start > end) (start, end) = (end, start);

        string nl = source.Contains("\r\n") ? "\r\n" : (source.Contains("\n") ? "\n" : "\r\n");
        int lineStart = start == 0 ? 0 : source.LastIndexOf('\n', start - 1) + 1;
        int nlIndex = source.IndexOf('\n', Math.Min(end, source.Length));
        int lineEnd = nlIndex < 0 ? source.Length : nlIndex;

        var body = source.Substring(lineStart, lineEnd - lineStart).TrimEnd('\r');
        var block = "```" + nl + body + nl + "```";
        var text = source.Substring(0, lineStart) + block + source.Substring(lineEnd);
        return (text, lineStart, lineStart + block.Length);
    }

    // 代码块开关:普通内容 → 包裹围栏;已处于围栏内 → 去掉围栏
    public static (string Text, int Start, int End) ToggleCodeBlock(string source, int start, int end)
    {
        if (start > end) (start, end) = (end, start);
        if (start < 0) start = 0;
        if (end > source.Length) end = source.Length;

        string nl = source.Contains("\r\n") ? "\r\n" : (source.Contains("\n") ? "\n" : "\r\n");

        var starts = new List<int> { 0 };
        var contentEnds = new List<int>();
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
            {
                contentEnds.Add(i > 0 && source[i - 1] == '\r' ? i - 1 : i);
                starts.Add(i + 1);
            }
        }
        contentEnds.Add(source.Length);
        int n = starts.Count;

        int LineOf(int off)
        {
            for (int k = n - 1; k >= 0; k--)
                if (off >= starts[k]) return k;
            return 0;
        }

        int firstLine = LineOf(start);
        int lastOff = end > start ? end - 1 : end;
        int lastLine = LineOf(Math.Min(lastOff, source.Length));

        bool IsFence(int idx)
        {
            if (idx < 0 || idx >= n) return false;
            var t = source.Substring(starts[idx], contentEnds[idx] - starts[idx]).TrimStart();
            return t.StartsWith("```") || t.StartsWith("~~~");
        }

        if (IsFence(firstLine - 1) && IsFence(lastLine + 1))
        {
            int openStart = starts[firstLine - 1];
            int afterOpen = starts[firstLine];
            int closeStart = starts[lastLine + 1];
            int afterClose = lastLine + 2 < n ? starts[lastLine + 2] : source.Length;

            int bodyEnd = closeStart;
            if (lastLine + 2 >= n)
            {
                if (bodyEnd > afterOpen && source[bodyEnd - 1] == '\n') bodyEnd--;
                if (bodyEnd > afterOpen && source[bodyEnd - 1] == '\r') bodyEnd--;
            }

            var text = source.Substring(0, openStart) + source.Substring(afterOpen, bodyEnd - afterOpen) + source.Substring(afterClose);
            return (text, openStart, openStart + (bodyEnd - afterOpen));
        }

        int lineStart = starts[firstLine];
        int bodyLast = contentEnds[lastLine];
        var bodyText = source.Substring(lineStart, bodyLast - lineStart);
        var wrapped = source.Substring(0, lineStart) + "```" + nl + bodyText + nl + "```" + source.Substring(bodyLast);
        int selStart = lineStart + 3 + nl.Length;
        return (wrapped, selStart, selStart + bodyText.Length);
    }
}

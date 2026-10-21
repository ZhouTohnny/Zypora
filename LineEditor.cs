using System.Text.RegularExpressions;

namespace Zypora;

public static class LineEditor
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})(?: (.*)|$)", RegexOptions.Compiled);
    private static readonly Regex UlPrefix = new(@"^(\s*)([-*+])( +)", RegexOptions.Compiled);
    private static readonly Regex OlPrefix = new(@"^(\s*)(\d+)([.)])( +)", RegexOptions.Compiled);

    // 判断某个偏移是否处于围栏代码块内部(``` 或 ~~~)
    public static bool IsInsideFence(string source, int offset)
    {
        if (offset < 0) offset = 0;
        if (offset > source.Length) offset = source.Length;

        int lineStart = offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1;
        bool inFence = false;
        foreach (var raw in source.Substring(0, lineStart).Split('\n'))
        {
            var t = raw.TrimStart();
            if (t.StartsWith("```") || t.StartsWith("~~~")) inFence = !inFence;
        }
        return inFence;
    }

    // 列表项回车:非空 → 延续同款列表标记(有序则序号 +1);空项 → 取消列表
    public static (bool Handled, string Text, int Caret) HandleListEnter(string source, int caret)
    {
        if (caret < 0) caret = 0;
        if (caret > source.Length) caret = source.Length;

        // 代码块内的回车不做列表处理(保持代码原样换行)
        if (IsInsideFence(source, caret)) return (false, source, caret);

        int lineStart = caret == 0 ? 0 : source.LastIndexOf('\n', caret - 1) + 1;
        int nlIndex = source.IndexOf('\n', caret);
        int lineEnd = nlIndex < 0 ? source.Length : nlIndex;
        var rawLine = source.Substring(lineStart, lineEnd - lineStart);
        bool hadCr = rawLine.EndsWith("\r");
        var core = hadCr ? rawLine.Substring(0, rawLine.Length - 1) : rawLine;

        var ul = UlPrefix.Match(core);
        var ol = OlPrefix.Match(core);
        if (!ul.Success && !ol.Success) return (false, source, caret);

        string newPrefix;
        if (ul.Success)
        {
            newPrefix = ul.Groups[1].Value + ul.Groups[2].Value + ul.Groups[3].Value;
        }
        else
        {
            int n = int.Parse(ol.Groups[2].Value);
            newPrefix = ol.Groups[1].Value + (n + 1).ToString() + ol.Groups[3].Value + ol.Groups[4].Value;
        }

        var prefixLen = ul.Success ? ul.Length : ol.Length;
        bool empty = core.Substring(prefixLen).Trim().Length == 0;

        if (empty)
        {
            return (true, source.Remove(lineStart, core.Length), lineStart);
        }

        string newline = hadCr || source.Contains("\r\n") ? "\r\n" : (source.Contains("\n") ? "\n" : "\r\n");
        int caretCol = Math.Clamp(caret - lineStart, 0, core.Length);
        var head = core.Substring(0, caretCol);
        var tail = core.Substring(caretCol);
        var rebuilt = head + newline + newPrefix + tail + (hadCr ? "\r" : "");
        var text = source.Substring(0, lineStart) + rebuilt + source.Substring(lineEnd);
        int newCaret = lineStart + head.Length + newline.Length + newPrefix.Length;
        return (true, text, newCaret);
    }

    public static (string Text, int Caret) TogglePrefix(
        string source, int caret, string mark, Regex detect, Regex strip)
    {
        var (text, start, _) = TogglePrefixRange(source, caret, caret, mark, detect, strip);
        return (text, start);
    }

    // 对 [start, end] 覆盖的所有行统一加/去前缀(空选区时即当前行)
    public static (string Text, int Start, int End) TogglePrefixRange(
        string source, int start, int end, string mark, Regex detect, Regex strip)
    {
        if (start > end) (start, end) = (end, start);

        var rawLines = source.Split('\n');
        var cores = new string[rawLines.Length];
        var hadCr = new bool[rawLines.Length];
        var starts = new int[rawLines.Length];
        int acc = 0;
        for (int i = 0; i < rawLines.Length; i++)
        {
            starts[i] = acc;
            acc += rawLines[i].Length + 1;
            hadCr[i] = rawLines[i].EndsWith("\r");
            cores[i] = hadCr[i] ? rawLines[i].Substring(0, rawLines[i].Length - 1) : rawLines[i];
        }

        int LineOf(int off)
        {
            for (int i = rawLines.Length - 1; i >= 0; i--)
                if (off >= starts[i]) return i;
            return 0;
        }

        int first = LineOf(start);
        int lastOff = end > start ? end - 1 : end;
        int last = LineOf(Math.Min(lastOff, source.Length));

        bool allMatch = true;
        for (int i = first; i <= last; i++)
            if (!detect.IsMatch(cores[i])) { allMatch = false; break; }

        var delta = new int[rawLines.Length];
        for (int i = first; i <= last; i++)
        {
            var line = cores[i];
            if (allMatch)
            {
                var m = strip.Match(line);
                int removed = m.Success ? m.Length : 0;
                if (removed > 0) cores[i] = strip.Replace(line, "", 1);
                delta[i] = -removed;
            }
            else if (detect.IsMatch(line))
            {
                delta[i] = 0;
            }
            else
            {
                cores[i] = mark + line;
                delta[i] = mark.Length;
            }
            rawLines[i] = cores[i] + (hadCr[i] ? "\r" : "");
        }

        int MapOffset(int off)
        {
            if (rawLines.Length == 0) return off;
            int k = LineOf(Math.Min(off, source.Length));
            int column = off - starts[k];
            int newColumn = Math.Clamp(column + delta[k], 0, cores[k].Length);
            int newStart = starts[k];
            for (int i = 0; i < k; i++) newStart += delta[i];
            return newStart + newColumn;
        }

        return (string.Join("\n", rawLines), MapOffset(start), MapOffset(end));
    }

    public static (string Text, int Caret) ToggleHeading(string source, int caret, int level)
    {
        var rawLines = source.Split('\n');
        int target = FindLine(rawLines, caret, out int lineStart);
        var rawLine = rawLines[target];
        bool hadCr = rawLine.EndsWith("\r");
        var line = hadCr ? rawLine.Substring(0, rawLine.Length - 1) : rawLine;

        var content = line.TrimStart(' ', '\t');
        int leading = line.Length - content.Length;
        var leadingStr = line.Substring(0, leading);
        int oldColumn = Math.Clamp(caret - lineStart, 0, line.Length);

        var m = HeadingRegex.Match(content);
        int delta;
        string updated;
        if (m.Success && m.Groups[1].Value.Length == level)
        {
            var body = m.Groups[2].Success ? m.Groups[2].Value : "";
            int oldMarker = content.Length - body.Length;
            updated = leadingStr + body;
            delta = -oldMarker;
        }
        else
        {
            var body = m.Success ? (m.Groups[2].Success ? m.Groups[2].Value : "") : content;
            int oldMarker = m.Success ? content.Length - body.Length : 0;
            updated = leadingStr + new string('#', level) + " " + body;
            delta = (level + 1) - oldMarker;
        }

        rawLines[target] = updated + (hadCr ? "\r" : "");
        int newColumn = Math.Clamp(oldColumn + delta, 0, updated.Length);
        return (string.Join("\n", rawLines), lineStart + newColumn);
    }

    public static int FindLine(string[] lines, int caret, out int lineStart)
    {
        int acc = 0;
        int lastStart = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            lastStart = acc;
            int len = lines[i].Length;
            if (caret >= acc && caret <= acc + len)
            {
                lineStart = acc;
                return i;
            }
            acc += len + 1;
        }
        lineStart = lastStart;
        return Math.Max(0, lines.Length - 1);
    }
}

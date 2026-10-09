using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zypora;
using WpfDoc = System.Windows.Documents.FlowDocument;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRange = System.Windows.Documents.TextRange;
using WpfUIC = System.Windows.Documents.InlineUIContainer;

internal static class T
{
    public static int Passed;
    public static int Failed;
    private static readonly List<string> Failures = new();

    public static void Ok(string name, bool cond, string? detail = null)
    {
        if (cond) { Passed++; return; }
        Failed++;
        Failures.Add(name + (detail == null ? "" : " :: " + detail));
        Console.WriteLine("FAIL " + name + (detail == null ? "" : " :: " + detail));
    }

    public static void Eq<T>(string name, T expected, T actual)
        => Ok(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected <{expected}> got <{actual}>");

    public static void Section(string title) => Console.WriteLine("\n== " + title + " ==");

    public static int Report()
    {
        Console.WriteLine($"\n{Passed} passed, {Failed} failed");
        if (Failed > 0)
        {
            Console.WriteLine("--- failures ---");
            foreach (var f in Failures) Console.WriteLine("  " + f);
        }
        return Failed == 0 ? 0 : 1;
    }
}

internal static class SearchTests
{
    private static readonly SearchOptions Plain = new(false, false, false);
    private static readonly SearchOptions Case = new(true, false, false);
    private static readonly SearchOptions Word = new(false, true, false);
    private static readonly SearchOptions Regex = new(false, false, true);

    private static string Show(IEnumerable<(int Start, int Length)> ms)
        => string.Join(",", ms.Select(m => $"{m.Start}:{m.Length}"));

    public static void Run()
    {
        T.Section("SearchService");

        var m1 = SearchService.Find("abc abc ABC", "abc", Plain);
        T.Eq("Find simple count", 3, m1.Count);
        T.Eq("Find simple offsets", "0:3,4:3,8:3", Show(m1));

        var m2 = SearchService.Find("abc abc ABC", "abc", Case);
        T.Eq("Find case-sensitive count", 2, m2.Count);

        var m3 = SearchService.Find("cat category cat.", "cat", Word);
        T.Eq("Find whole-word offsets", "0:3,13:3", Show(m3));

        var m4 = SearchService.Find("a1 b22 c333", @"\d+", Regex);
        T.Eq("Find regex offsets", "1:1,4:2,8:3", Show(m4));

        var m5 = SearchService.Find("abc", "[", Regex);
        T.Eq("Find invalid regex is empty", 0, m5.Count);

        T.Eq("Find empty query", 0, SearchService.Find("abc", "", Plain).Count);
        T.Eq("Find null-ish query", 0, SearchService.Find("abc", "   ", Plain).Count);

        var m6 = SearchService.Find("aaaa", "aa", Plain);
        T.Eq("Find non-overlapping", "0:2,2:2", Show(m6));

        var m7 = SearchService.Find("abc", "x*", Regex);
        T.Eq("Find zero-length skipped", 0, m7.Count);

        var m8 = SearchService.Find("cat category", "cat", new SearchOptions(false, true, true));
        T.Eq("Find regex+whole-word", "0:3", Show(m8));

        var (r1, c1) = SearchService.ReplaceOne("hello world", (6, 5), "there");
        T.Eq("ReplaceOne text", "hello there", r1);
        T.Eq("ReplaceOne caret", 11, c1);

        var (r2, n2) = SearchService.ReplaceAll("a a a", "a", "b", Plain);
        T.Eq("ReplaceAll text", "b b b", r2);
        T.Eq("ReplaceAll count", 3, n2);

        var (r3, n3) = SearchService.ReplaceAll("a A a", "a", "b", Case);
        T.Eq("ReplaceAll case text", "b A b", r3);
        T.Eq("ReplaceAll case count", 2, n3);

        var (r4, n4) = SearchService.ReplaceAll("a1 b2", @"(\w)(\d)", "$2$1", Regex);
        T.Eq("ReplaceAll regex groups", "1a 2b", r4);
        T.Eq("ReplaceAll regex count", 2, n4);

        var (r5, n5) = SearchService.ReplaceAll("abc", "", "x", Plain);
        T.Eq("ReplaceAll empty query text", "abc", r5);
        T.Eq("ReplaceAll empty query count", 0, n5);

        var (r6, n6) = SearchService.ReplaceAll("abc", "[", "x", Regex);
        T.Eq("ReplaceAll invalid regex text", "abc", r6);
        T.Eq("ReplaceAll invalid regex count", 0, n6);

        var m9 = SearchService.Find("a.b axb", ".", Plain);
        T.Eq("Find literal dot", "1:1", Show(m9));

        var m10 = SearchService.Find("cat_cat cat", "cat", Word);
        T.Eq("Find whole-word underscore", "8:3", Show(m10));
    }
}

internal static class TableTests
{
    private static string Cells(IEnumerable<TableCell> cs) => string.Join("|", cs.Select(c => c.Text));
    private static string Aligns(IEnumerable<ColumnAlign> a) => string.Join(",", a);

    public static void Run()
    {
        T.Section("TableParser");

        T.Ok("sep basic", TableParser.IsSeparatorLine("|---|---|"));
        T.Ok("sep with colons", TableParser.IsSeparatorLine("|:--|:-:|--:|"));
        T.Ok("sep no pipes", TableParser.IsSeparatorLine("--- | ---"));
        T.Ok("sep single dash", TableParser.IsSeparatorLine("| - | - |"));
        T.Ok("sep rejects header", !TableParser.IsSeparatorLine("| a | b |"));
        T.Ok("sep rejects empty", !TableParser.IsSeparatorLine(""));
        T.Ok("sep rejects text", !TableParser.IsSeparatorLine("---"));

        T.Eq("split leading/trailing", "a|b", string.Join("|", TableParser.SplitCells("| a | b |")));
        T.Eq("split no edges", "a|b", string.Join("|", TableParser.SplitCells("a | b")));
        T.Eq("split empty middle", "a||c", string.Join("|", TableParser.SplitCells("| a || c |")));
        T.Eq("split escaped pipe", "a | b|c", string.Join("|", TableParser.SplitCells(@"| a \| b | c |")));

        var lines1 = new[] { "| a | b |", "|---|---|", "| 1 | 2 |" };
        T.Ok("parse basic ok", TableParser.TryParse(lines1, 0, out var m1, out var e1));
        T.Eq("parse basic end", 3, e1);
        T.Eq("parse basic cols", 2, m1.ColumnCount);
        T.Eq("parse basic rows", 2, m1.Rows.Count);
        T.Eq("parse header cells", "a|b", Cells(m1.Rows[0].Cells));
        T.Eq("parse data cells", "1|2", Cells(m1.Rows[1].Cells));
        T.Eq("parse basic align", "None,None", Aligns(m1.Align));
        T.Eq("parse raw line kept", "| 1 | 2 |", m1.Rows[1].RawLine);

        var lines2 = new[] { "| L | C | R |", "|:--|:-:|--:|", "| 1 | 2 | 3 |" };
        T.Ok("parse align ok", TableParser.TryParse(lines2, 0, out var m2, out _));
        T.Eq("parse align", "Left,Center,Right", Aligns(m2.Align));

        var lines3 = new[] { "a | b", "--- | ---", "1 | 2" };
        T.Ok("parse no-edge ok", TableParser.TryParse(lines3, 0, out var m3, out _));
        T.Eq("parse no-edge header", "a|b", Cells(m3.Rows[0].Cells));

        var lines4 = new[] { "| a | b | c |", "| - | - | - |", "| 1 | 2 |" };
        T.Ok("parse ragged ok", TableParser.TryParse(lines4, 0, out var m4, out _));
        T.Eq("parse ragged cols", 3, m4.ColumnCount);
        T.Eq("parse ragged padded", "1|2|", Cells(m4.Rows[1].Cells));

        var lines5 = new[] { "| a | b |", "| - | - |", "| 1 | 2 |", "after" };
        T.Ok("parse stop ok", TableParser.TryParse(lines5, 0, out var m5, out var e5));
        T.Eq("parse stop end", 3, e5);
        T.Eq("parse stop rows", 2, m5.Rows.Count);

        var lines6 = new[] { "intro", "| a | b |", "| - | - |" };
        T.Ok("parse offset ok", TableParser.TryParse(lines6, 1, out _, out var e6));
        T.Eq("parse offset end", 3, e6);

        T.Ok("parse no sep false", !TableParser.TryParse(new[] { "| a | b |", "plain" }, 0, out _, out _));
        T.Ok("parse no pipe false", !TableParser.TryParse(new[] { "hello", "world" }, 0, out _, out _));
        T.Ok("parse empty false", !TableParser.TryParse(new[] { "", "" }, 0, out _, out _));
        T.Ok("parse out-of-range false", !TableParser.TryParse(new[] { "| a | b |" }, 0, out _, out _));

        var (tt, ts, te) = TableParser.InsertTemplate("", 0, 3, 2);
        T.Eq("tmpl text", "| 列 1 | 列 2 | 列 3 |\r\n|---|---|---|\r\n|  |  |  |\r\n|  |  |  |\r\n", tt);
        T.Eq("tmpl selection", "2,5", $"{ts},{te}");

        var (tt2, _, _) = TableParser.InsertTemplate("hello", 0, 2, 1);
        T.Ok("tmpl inserts at line start", tt2.StartsWith("| 列 1 | 列 2 |\r\n|---|---|\r\n|  |  |\r\nhello"));

        var tlines = tt.Split('\n');
        T.Ok("tmpl parses", TableParser.TryParse(tlines, 0, out var tm, out _));
        T.Eq("tmpl cols", 3, tm.ColumnCount);
        T.Eq("tmpl rows", 3, tm.Rows.Count);

        T.Eq("tmpl identity", tt, EditableRenderer.ReadSource(EditableRenderer.BuildPreview(tt)));
    }
}

internal static class RendererTests
{
    internal static List<WpfUIC> Containers(WpfDoc doc)
    {
        var list = new List<WpfUIC>();
        foreach (var b in doc.Blocks)
            if (b is WpfParagraph p)
                foreach (var inl in p.Inlines)
                    if (inl is WpfUIC c) list.Add(c);
        return list;
    }

    private static void Walk(object o, List<TextBlock> acc)
    {
        if (o is TextBlock tb) { acc.Add(tb); return; }
        if (o is DependencyObject d)
            foreach (var child in LogicalTreeHelper.GetChildren(d))
                Walk(child, acc);
    }

    private static List<string> GridTexts(WpfUIC c)
    {
        var blocks = new List<TextBlock>();
        Walk(c.Child, blocks);
        return blocks.Select(tb => new WpfRange(tb.ContentStart, tb.ContentEnd).Text).ToList();
    }

    private static string Identity(string src)
    {
        var preview = EditableRenderer.ReadSource(EditableRenderer.BuildPreview(src));
        var raw = EditableRenderer.ReadSource(EditableRenderer.BuildRaw(src));
        if (preview != src) return "preview mismatch";
        if (raw != src) return "raw mismatch";
        return "";
    }

    internal static List<T> Collect<T>(object o) where T : class
    {
        var list = new List<T>();
        void W(object x)
        {
            if (x is T t) { list.Add(t); return; }
            if (x is DependencyObject d)
                foreach (var c in LogicalTreeHelper.GetChildren(d)) W(c);
        }
        W(o);
        return list;
    }

    public static void Run()
    {
        T.Section("Table rendering");

        var src1 = "| a | b |\r\n|---|---|\r\n| 1 | 2 |";
        var doc1 = EditableRenderer.BuildPreview(src1);
        var cs1 = Containers(doc1);
        T.Eq("table row containers", 2, cs1.Count);
        T.Eq("table header cells", "a,b", string.Join(",", GridTexts(cs1[0])));
        T.Eq("table data cells", "1,2", string.Join(",", GridTexts(cs1[1])));

        var src2 = "| L | C | R |\r\n|:--|:-:|--:|\r\n| 1 | 2 | 3 |";
        var cs2 = Containers(EditableRenderer.BuildPreview(src2));
        var header = new List<TextBlock>();
        Walk(cs2[0].Child, header);
        T.Eq("align left", TextAlignment.Left, header[0].TextAlignment);
        T.Eq("align center", TextAlignment.Center, header[1].TextAlignment);
        T.Eq("align right", TextAlignment.Right, header[2].TextAlignment);
        T.Ok("header bold", header.All(tb => tb.FontWeight == FontWeights.Bold));

        var src3 = "|a|b|\r\n|-|-|\r\n|1|2|";
        T.Eq("compact table no containers", 0, Containers(EditableRenderer.BuildPreview(src3)).Count);
        T.Eq("compact table identity", "", Identity(src3));

        T.Eq("identity basic", "", Identity(src1));
        T.Eq("identity align", "", Identity(src2));
        T.Eq("identity ragged", "", Identity("| a | b | c |\r\n| - | - | - |\r\n| 1 | 2 |"));
        T.Eq("identity no-edge", "", Identity("a | b\r\n--- | ---\r\n1 | 2"));
        T.Eq("identity table then text", "", Identity("| a | b |\r\n| - | - |\r\n| 1 | 2 |\r\ntail"));

        var srcC = "| a | b |\r\n|---|---|\r\n| 1 | 2 |";
        var docC = EditableRenderer.BuildPreview(srcC);
        int bad = 0;
        var badOffsets = new List<string>();
        for (int off = 0; off <= srcC.Length; off++)
        {
            if (off > 0 && srcC[off - 1] == '\r' && off < srcC.Length && srcC[off] == '\n') continue;
            var ptr = DocumentCaret.GetPointerAtCharOffset(docC, off);
            int got = DocumentCaret.GetCaretOffset(docC, ptr);
            if (got != off) { bad++; badOffsets.Add($"{off}->{got}"); }
        }
        T.Eq("table caret mapping", 0, bad);
        if (bad > 0) Console.WriteLine("  bad offsets: " + string.Join(", ", badOffsets));

        T.Section("Image rendering");

        var srcImg = "![image 1](C:/nope/x.png)";
        var cImg = Containers(EditableRenderer.BuildPreview(srcImg));
        T.Eq("image container", 1, cImg.Count);
        T.Ok("image placeholder shows src", GridTexts(cImg[0]).Any(t => t.Contains("x.png")));
        T.Eq("image identity", "", Identity(srcImg));

        var srcNoSpace = "![a](x.png)";
        T.Eq("image no-space no container", 0, Containers(EditableRenderer.BuildPreview(srcNoSpace)).Count);
        T.Eq("image no-space identity", "", Identity(srcNoSpace));

        var docI = EditableRenderer.BuildPreview(srcImg);
        int badI = 0;
        for (int off = 0; off <= srcImg.Length; off++)
        {
            var ptr = DocumentCaret.GetPointerAtCharOffset(docI, off);
            if (DocumentCaret.GetCaretOffset(docI, ptr) != off) badI++;
        }
        T.Eq("image caret mapping", 0, badI);

        var imgRoot = Path.Combine(Path.GetTempPath(), "zypora-imgrender-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(imgRoot, "assets"));
        var pngPath = Path.Combine(imgRoot, "assets", "ok.png");
        File.WriteAllBytes(pngPath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="));
        var cOk = Containers(EditableRenderer.BuildPreview("![image 1](assets/ok.png)", null, imgRoot));
        T.Eq("image loaded container", 1, cOk.Count);
        var loaded = Collect<Image>(cOk[0].Child);
        T.Ok("image element loaded", loaded.Count == 1 && loaded[0].Source != null);
        T.Eq("image loaded identity", "", Identity("![image 1](assets/ok.png)"));
        try { Directory.Delete(imgRoot, true); } catch { }

        T.Section("Identity fuzz");

        var pool = new[]
        {
            "| a | b |", "|---|---|", "| 1 | 2 |", "| x | y | z |", "|:--|:-:|--:|",
            "| --- | --- |", "|a|b|", "| p | q |", "# Heading", "## Sub", "plain text",
            "**bold** and *italic*", "`code`", "- item", "1. one", "> quote", "---", "",
            "```", "code line", "```", "text | with pipe", "  indented",
            "![image 1](a.png)", "![a](b.png)", "![image 2](C:/x/y.png)"
        };

        var rng = new Random(12345);
        int fuzzFail = 0;
        string? firstFail = null;
        for (int t = 0; t < 500; t++)
        {
            int n = 1 + rng.Next(8);
            var lines = new string[n];
            for (int i = 0; i < n; i++) lines[i] = pool[rng.Next(pool.Length)];
            var src = string.Join("\r\n", lines);
            var err = Identity(src);
            if (err != "")
            {
                fuzzFail++;
                firstFail ??= err + " :: " + src.Replace("\r", "\\r").Replace("\n", "\\n");
            }
        }
        T.Eq("fuzz identity failures", 0, fuzzFail);
        if (firstFail != null) Console.WriteLine("  first fuzz failure: " + firstFail);
    }
}

internal static class UndoTests
{
    private static string Show((string Text, int Caret)? s) => s == null ? "<null>" : $"{s.Value.Text}@{s.Value.Caret}";

    public static void Run()
    {
        T.Section("UndoHistory");
        var h = new UndoHistory();
        h.Reset("a", 1);
        T.Ok("history no undo at reset", !h.CanUndo);
        T.Ok("history no redo at reset", !h.CanRedo);
        h.Push("ab", 2);
        h.Push("ab", 2);
        h.Push("abc", 3);
        T.Eq("history undo1", "ab@2", Show(h.Undo()));
        T.Eq("history undo2", "a@1", Show(h.Undo()));
        T.Ok("history no more undo", !h.CanUndo);
        T.Eq("history redo1", "ab@2", Show(h.Redo()));
        T.Eq("history redo2", "abc@3", Show(h.Redo()));
        T.Ok("history no more redo", !h.CanRedo);

        var h2 = new UndoHistory();
        h2.Reset("a", 0);
        h2.Push("ab", 1);
        h2.Push("ab", 2);
        h2.Undo();
        T.Eq("history dedup caret", "ab@2", Show(h2.Redo()));

        T.Section("UndoCoordinator");

        var c1 = new UndoCoordinator();
        c1.Reset("a", 1);
        c1.Change("ab", 2);
        c1.Change("abc", 3);
        T.Eq("group undo once", "a@1", Show(c1.Undo("abc", 3)));
        T.Eq("group redo", "abc@3", Show(c1.Redo()));
        T.Ok("group canundo", c1.CanUndo);

        var c2 = new UndoCoordinator();
        c2.Reset("a", 1);
        c2.Change("ab", 2);
        c2.Command("ab!", 3);
        T.Eq("command undo1", "ab@2", Show(c2.Undo("ab!", 3)));
        T.Eq("command undo2", "a@1", Show(c2.Undo("ab", 2)));

        var c3 = new UndoCoordinator();
        c3.Reset("", 0);
        c3.Change("x", 1);
        c3.Commit();
        c3.Change("xy", 2);
        c3.Commit();
        T.Eq("commit undo1", "x@1", Show(c3.Undo("xy", 2)));
        T.Eq("commit undo2", "@0", Show(c3.Undo("x", 1)));

        var c4 = new UndoCoordinator();
        c4.Reset("base", 4);
        c4.Change("base+", 5);
        c4.Change("base++", 6);
        T.Eq("pending undo", "base@4", Show(c4.Undo("base++", 6)));
        T.Eq("pending redo", "base++@6", Show(c4.Redo()));

        var c5 = new UndoCoordinator();
        c5.Reset("solo", 4);
        T.Eq("empty undo null", "<null>", Show(c5.Undo("solo", 4)));
        T.Eq("empty redo null", "<null>", Show(c5.Redo()));

        var c6 = new UndoCoordinator();
        c6.Reset("a", 1);
        c6.Change("ab", 2);
        c6.Commit();
        c6.Undo("ab", 2);
        c6.Change("aZ", 2);
        c6.Commit();
        T.Eq("redo cleared", "<null>", Show(c6.Redo()));
    }
}

internal static class InlineTests
{
    public static void Run()
    {
        T.Section("InlineEditor");

        T.Eq("bold wrap", "**hello**", InlineEditor.Toggle("hello", 0, 5, "**").Text);
        var bw = InlineEditor.Toggle("hello", 0, 5, "**");
        T.Eq("bold wrap sel", "2,7", $"{bw.Start},{bw.End}");
        var ub = InlineEditor.Toggle("**hello**", 2, 7, "**");
        T.Eq("bold unwrap", "hello", ub.Text);
        T.Eq("bold unwrap sel", "0,5", $"{ub.Start},{ub.End}");

        T.Eq("italic wrap", "*hi*", InlineEditor.Toggle("hi", 0, 2, "*").Text);
        var ui = InlineEditor.Toggle("*hi*", 1, 3, "*");
        T.Eq("italic unwrap", "hi", ui.Text);

        var es = InlineEditor.Toggle("abc", 1, 1, "**");
        T.Eq("toggle empty selection", "abc", es.Text);

        var (t1, c1) = InlineEditor.ToggleAtCaret("ab", 1, "**");
        T.Eq("atcaret insert", "a****b", t1);
        T.Eq("atcaret insert caret", 3, c1);
        var (t2, c2) = InlineEditor.ToggleAtCaret("a****b", 3, "**");
        T.Eq("atcaret remove", "ab", t2);
        T.Eq("atcaret remove caret", 1, c2);

        var (f1, _, _) = InlineEditor.WrapFence("a\r\nb", 0, 3);
        T.Eq("wrapfence text", "```\r\na\r\nb\r\n```", f1);

        var (cb, _, _) = InlineEditor.ToggleCodeBlock("hello", 0, 5);
        T.Eq("codeblock wrap", "```\r\nhello\r\n```", cb);
        var (cu, _, _) = InlineEditor.ToggleCodeBlock("```\r\nhello\r\n```", 7, 12);
        T.Eq("codeblock unwrap", "hello", cu);
    }
}

internal static class LineTests
{
    private static readonly System.Text.RegularExpressions.Regex Ul = new(@"^\s*[-*+]\s+");
    private static readonly System.Text.RegularExpressions.Regex Ol = new(@"^\s*\d+[.)]\s+");

    public static void Run()
    {
        T.Section("LineEditor");

        T.Eq("ul add", "- hello", LineEditor.TogglePrefix("hello", 0, "- ", Ul, Ul).Text);
        T.Eq("ul add caret", 2, LineEditor.TogglePrefix("hello", 0, "- ", Ul, Ul).Caret);
        T.Eq("ul remove", "hello", LineEditor.TogglePrefix("- hello", 0, "- ", Ul, Ul).Text);

        var mr = LineEditor.TogglePrefixRange("a\r\nb", 0, 4, "- ", Ul, Ul);
        T.Eq("ul multi", "- a\r\n- b", mr.Text);
        T.Eq("ul mixed", "- a\r\n- b", LineEditor.TogglePrefixRange("- a\r\nb", 0, 6, "- ", Ul, Ul).Text);

        T.Eq("h1 add", "# hello", LineEditor.ToggleHeading("hello", 0, 1).Text);
        T.Eq("h1 remove", "hello", LineEditor.ToggleHeading("# hello", 2, 1).Text);
        T.Eq("h2 from h1", "## hi", LineEditor.ToggleHeading("# hi", 2, 2).Text);

        var le1 = LineEditor.HandleListEnter("- item", 6);
        T.Ok("list enter handled", le1.Handled);
        T.Eq("list enter text", "- item\r\n- ", le1.Text);
        var le2 = LineEditor.HandleListEnter("- ", 2);
        T.Ok("list empty handled", le2.Handled);
        T.Eq("list empty text", "", le2.Text);
        var le3 = LineEditor.HandleListEnter("3. x", 4);
        T.Eq("ordered increment", "3. x\r\n4. ", le3.Text);
        T.Ok("list enter plain false", !LineEditor.HandleListEnter("plain", 5).Handled);
        T.Ok("list enter in fence false", !LineEditor.HandleListEnter("```\r\n- x", 8).Handled);

        T.Ok("fence inside", LineEditor.IsInsideFence("```\r\ncode", 7));
        T.Ok("fence outside", !LineEditor.IsInsideFence("code", 2));
        T.Ok("fence after close", !LineEditor.IsInsideFence("```\r\ncode\r\n```\r\nx", 16));
    }
}

internal static class CaretTests
{
    private static int RoundTripBad(string src)
    {
        var doc = EditableRenderer.BuildPreview(src);
        int bad = 0;
        for (int off = 0; off <= src.Length; off++)
        {
            if (off > 0 && src[off - 1] == '\r' && off < src.Length && src[off] == '\n') continue;
            var ptr = DocumentCaret.GetPointerAtCharOffset(doc, off);
            if (DocumentCaret.GetCaretOffset(doc, ptr) != off) bad++;
        }
        return bad;
    }

    public static void Run()
    {
        T.Section("DocumentCaret");
        T.Eq("caret plain", 0, RoundTripBad("hello world"));
        T.Eq("caret multiline", 0, RoundTripBad("a\r\nb\r\nc"));
        T.Eq("caret inline marks", 0, RoundTripBad("**bold** and *it* and `code`"));
        T.Eq("caret headings lists", 0, RoundTripBad("# H\r\n- a\r\n1. b\r\n> q"));
        T.Eq("caret code fence", 0, RoundTripBad("```\r\ncode\r\n```"));
    }
}

internal static class FeatureTests
{
    public static void Run()
    {
        T.Section("Renderer features");

        var h = (WpfParagraph)EditableRenderer.BuildPreview("# Title").Blocks.FirstBlock!;
        T.Eq("heading font size", 30.0, h.FontSize);
        T.Ok("heading marker hidden", h.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.FontSize == 1));

        var b = (WpfParagraph)EditableRenderer.BuildPreview("a **bold** b").Blocks.FirstBlock!;
        T.Ok("bold run exists", b.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.FontWeight == FontWeights.Bold));

        var q = (WpfParagraph)EditableRenderer.BuildPreview("> quote").Blocks.FirstBlock!;
        T.Eq("quote border", 3.0, q.BorderThickness.Left);

        var hr = (WpfParagraph)EditableRenderer.BuildPreview("---").Blocks.FirstBlock!;
        T.Eq("hr border", 1.0, hr.BorderThickness.Bottom);

        T.Ok("bullet container", RendererTests.Containers(EditableRenderer.BuildPreview("- item")).Count >= 1);

        var code = EditableRenderer.BuildPreview("```\r\nx\r\n```");
        T.Ok("code monospace", code.Blocks.OfType<WpfParagraph>().Any(p => p.FontFamily.Source == "Consolas"));

        T.Eq("identity empty fence", "", IdentityOnly("```\r\n```"));
        T.Eq("identity unclosed fence", "", IdentityOnly("```\r\ncode"));
        T.Eq("identity spaces line", "", IdentityOnly("a\r\n   \r\nb"));
    }

    private static string IdentityOnly(string src)
    {
        var preview = EditableRenderer.ReadSource(EditableRenderer.BuildPreview(src));
        var raw = EditableRenderer.ReadSource(EditableRenderer.BuildRaw(src));
        return preview == src && raw == src ? "" : "mismatch";
    }
}

internal static class PdfTests
{
    public static void Run()
    {
        T.Section("PdfExporter");
        try
        {
            var bytes = PdfExporter.Export("# 标题\r\n\r\n正文 **粗** *斜* `码`\r\n\r\n- a\r\n- b\r\n\r\n1. x\r\n\r\n> q\r\n\r\n```\r\ncode\r\n```\r\n\r\n---");
            T.Ok("pdf non-empty", bytes.Length > 500);
            T.Eq("pdf header", "%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        }
        catch (Exception ex)
        {
            T.Ok("pdf export", false, ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            var empty = PdfExporter.Export("");
            T.Ok("pdf empty doc", empty.Length > 100);
        }
        catch (Exception ex)
        {
            T.Ok("pdf empty doc", false, ex.GetType().Name + ": " + ex.Message);
        }
    }
}

internal static class ImageTests
{
    public static void Run()
    {
        T.Section("ImageSupport");

        T.Ok("img ext png", ImageSupport.IsImageExtension("a.png"));
        T.Ok("img ext upper", ImageSupport.IsImageExtension("a.PNG"));
        T.Ok("img ext jpeg", ImageSupport.IsImageExtension("a.jpeg"));
        T.Ok("img ext gif", ImageSupport.IsImageExtension("a.gif"));
        T.Ok("img ext bmp", ImageSupport.IsImageExtension("a.bmp"));
        T.Ok("img ext txt false", !ImageSupport.IsImageExtension("a.txt"));
        T.Ok("img ext none false", !ImageSupport.IsImageExtension("a"));
        T.Ok("img ext double false", !ImageSupport.IsImageExtension("a.png.txt"));

        T.Ok("parse image", ImageSupport.TryParseImageLine("![alt](a.png)", out var a1, out var s1));
        T.Eq("parse alt", "alt", a1);
        T.Eq("parse src", "a.png", s1);
        T.Ok("parse empty alt", ImageSupport.TryParseImageLine("  ![](x.png)  ", out var a2, out var s2));
        T.Eq("parse empty alt val", "", a2);
        T.Eq("parse src trimmed", "x.png", s2);
        T.Ok("parse alt with space", ImageSupport.TryParseImageLine("![image 1](C:/a/b.png)", out var a3, out var s3));
        T.Eq("parse alt space val", "image 1", a3);
        T.Eq("parse src path", "C:/a/b.png", s3);
        T.Ok("parse rejects inline", !ImageSupport.TryParseImageLine("text ![a](b.png)", out _, out _));
        T.Ok("parse rejects trailing", !ImageSupport.TryParseImageLine("![a](b.png) extra", out _, out _));
        T.Ok("parse rejects plain", !ImageSupport.TryParseImageLine("not image", out _, out _));
        T.Ok("parse rejects empty", !ImageSupport.TryParseImageLine("", out _, out _));

        T.Eq("name keep", "photo_3f9a2c1b.jpg", ImageSupport.MakeAssetFileName("photo.jpg", "3f9a2c1b"));
        T.Eq("name null", "clip_3f9a2c1b.png", ImageSupport.MakeAssetFileName(null, "3f9a2c1b"));
        T.Eq("name empty", "clip_3f9a2c1b.png", ImageSupport.MakeAssetFileName("", "3f9a2c1b"));
        T.Eq("name no ext", "noext_abcd1234.png", ImageSupport.MakeAssetFileName("noext", "abcd1234"));
        T.Eq("name sanitize", "abc_abcd1234.png", ImageSupport.MakeAssetFileName("a*b?c.png", "abcd1234"));
        T.Eq("name keep ext case", "Photo_t.JPG", ImageSupport.MakeAssetFileName("Photo.JPG", "t"));

        T.Ok("remote http", ImageSupport.IsRemote("http://a/b.png"));
        T.Ok("remote https", ImageSupport.IsRemote("https://a/b.png"));
        T.Ok("remote local false", !ImageSupport.IsRemote("a/b.png"));

        var (it1, ic1) = ImageSupport.InsertImageLines("hello", 0, new[] { "C:/a/x.png" });
        T.Eq("insert text", "![image 1](C:/a/x.png)\r\nhello", it1);
        T.Eq("insert caret", "![image 1](C:/a/x.png)".Length, ic1);
        var (it2, _) = ImageSupport.InsertImageLines("a\r\nb", 3, new[] { "C:/a/x.png" });
        T.Eq("insert at line start", "a\r\n![image 1](C:/a/x.png)\r\nb", it2);
        var (it3, _) = ImageSupport.InsertImageLines("![image 2](old.png)\r\nx", 0, new[] { "C:/a/x.png" });
        T.Ok("insert next index", it3.Contains("![image 3](C:/a/x.png)"));
        var (it4, _) = ImageSupport.InsertImageLines("", 0, new[] { "C:/a/x.png", "C:/a/y.png" });
        T.Eq("insert multiple", "![image 1](C:/a/x.png)\r\n![image 2](C:/a/y.png)\r\n", it4);
        var (it5, _) = ImageSupport.InsertImageLines("", 0, new[] { @"C:\a\x.png" });
        T.Ok("insert forward slashes", it5.Contains("C:/a/x.png"));
        T.Eq("next index none", 1, ImageSupport.NextImageIndex("no images"));
        T.Eq("next index max", 4, ImageSupport.NextImageIndex("![image 3](a) ![image 1](b)"));

        var root = Path.Combine(Path.GetTempPath(), "zypora-imgtest-" + Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(root, "app");
        var assets = Path.Combine(appDir, "assets");
        var docDir = Path.Combine(root, "docs");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(docDir);
        var appImg = Path.Combine(assets, "x.png");
        var docImg = Path.Combine(docDir, "y.png");
        File.WriteAllText(appImg, "x");
        File.WriteAllText(docImg, "y");

        T.Eq("resolve absolute", docImg, ImageSupport.ResolveImagePath(docImg, null, appDir));
        T.Eq("resolve doc-relative", docImg, ImageSupport.ResolveImagePath("y.png", docDir, appDir));
        T.Eq("resolve fallback-by-name", appImg, ImageSupport.ResolveImagePath("C:/old/assets/x.png", null, appDir));
        T.Eq("resolve missing returns src", "nope.png", ImageSupport.ResolveImagePath("nope.png", docDir, appDir));
        var fwd = docImg.Replace('\\', '/');
        T.Eq("resolve forward slashes", docImg, ImageSupport.ResolveImagePath(fwd, null, appDir));

        try { Directory.Delete(root, true); } catch { }

        T.Section("Image assets");
        var aroot = Path.Combine(Path.GetTempPath(), "zypora-assets-" + Guid.NewGuid().ToString("N"));
        var assets2 = Path.Combine(aroot, "assets");
        Directory.CreateDirectory(aroot);

        var srcFile = Path.Combine(aroot, "src.png");
        File.WriteAllBytes(srcFile, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var copied = ImageSupport.CopyFileToAssets(srcFile, assets2, "tok12345");
        T.Ok("copy file exists", File.Exists(copied));
        T.Eq("copy file name", "src_tok12345.png", Path.GetFileName(copied));

        var bmp = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        var savedBmp = ImageSupport.SaveBitmapToAssets(bmp, assets2, "clip1234");
        T.Ok("bitmap saved exists", File.Exists(savedBmp));
        T.Eq("bitmap name", "clip_clip1234.png", Path.GetFileName(savedBmp));
        T.Ok("bitmap non-empty", new FileInfo(savedBmp).Length > 50);

        var appRoot = Path.Combine(Path.GetTempPath(), "zypora-e2e-" + Guid.NewGuid().ToString("N"));
        var savedPath = ImageSupport.SaveBitmapToAssets(new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null),
            Path.Combine(appRoot, "assets"), "e2e00001");
        var (e2eText, _) = ImageSupport.InsertImageLines("", 0, new[] { savedPath });
        var e2eDoc = EditableRenderer.BuildPreview(e2eText, null, appRoot);
        var e2eContainers = RendererTests.Containers(e2eDoc);
        T.Eq("e2e container", 1, e2eContainers.Count);
        var e2eImgs = RendererTests.Collect<Image>(e2eContainers[0].Child);
        T.Ok("e2e image loaded", e2eImgs.Count == 1 && e2eImgs[0].Source != null);
        T.Eq("e2e identity", e2eText, EditableRenderer.ReadSource(e2eDoc));

        try { Directory.Delete(aroot, true); } catch { }
        try { Directory.Delete(appRoot, true); } catch { }
    }
}

internal static class AppInfoTests
{
    public static void Run()
    {
        T.Section("App info");
        var asm = typeof(Zypora.MainWindow).Assembly;
        var ver = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        T.Ok("app version is 1.0.1", ver != null && ver.StartsWith("1.0.1"), "ver=" + ver);

        T.Eq("version suffix strips hash", " v1.0.1", AppInfo.VersionSuffix("1.0.1+bcf662f"));
        T.Eq("version suffix plain", " v1.0.1", AppInfo.VersionSuffix("1.0.1"));
        T.Eq("version suffix empty", "", AppInfo.VersionSuffix(""));
        T.Eq("version suffix null", "", AppInfo.VersionSuffix(null));

        T.Section("StartupArgs");
        var tmp = Path.Combine(Path.GetTempPath(), "zypora-args-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(tmp, "# hi");
        var got = StartupArgs.OpenableFiles(new[] { "nope.md", tmp, "" });
        T.Eq("args filter count", 1, got.Count);
        T.Eq("args filter value", tmp, got[0]);
        T.Eq("args none", 0, StartupArgs.OpenableFiles(new[] { "", "nope.md" }).Count);
        File.Delete(tmp);
    }
}

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        RunGroup("SearchService", SearchTests.Run);
        RunGroup("TableParser", TableTests.Run);
        RunGroup("Renderer", RendererTests.Run);
        RunGroup("Undo", UndoTests.Run);
        RunGroup("InlineEditor", InlineTests.Run);
        RunGroup("LineEditor", LineTests.Run);
        RunGroup("DocumentCaret", CaretTests.Run);
        RunGroup("Features", FeatureTests.Run);
        RunGroup("PdfExporter", PdfTests.Run);
        RunGroup("ImageSupport", ImageTests.Run);
        RunGroup("AppInfo", AppInfoTests.Run);
        return T.Report();
    }

    private static void RunGroup(string name, Action body)
    {
        try { body(); }
        catch (Exception ex) { T.Ok(name + " threw", false, ex.GetType().Name + ": " + ex.Message); }
    }
}

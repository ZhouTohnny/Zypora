using System.Text.RegularExpressions;

namespace Zypora;

public sealed record SearchOptions(bool CaseSensitive, bool WholeWord, bool UseRegex);

public static class SearchService
{
    public static IReadOnlyList<(int Start, int Length)> Find(string source, string query, SearchOptions options)
    {
        var result = new List<(int, int)>();
        if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(query)) return result;

        Regex regex;
        try { regex = BuildRegex(query, options); }
        catch (ArgumentException) { return result; }

        foreach (Match m in regex.Matches(source))
        {
            if (m.Length == 0) continue;
            result.Add((m.Index, m.Length));
        }
        return result;
    }

    public static (string Text, int NewCaret) ReplaceOne(string source, (int Start, int Length) match, string replacement)
    {
        if (match.Start < 0 || match.Start + match.Length > source.Length)
            return (source, match.Start);

        var text = source.Remove(match.Start, match.Length).Insert(match.Start, replacement);
        return (text, match.Start + replacement.Length);
    }

    public static (string Text, int Count) ReplaceAll(string source, string query, string replacement, SearchOptions options)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(query)) return (source, 0);

        Regex regex;
        try { regex = BuildRegex(query, options); }
        catch (ArgumentException) { return (source, 0); }

        int count = 0;
        var text = regex.Replace(source, m =>
        {
            if (m.Length == 0) return m.Value;
            count++;
            return options.UseRegex ? m.Result(replacement) : replacement;
        });
        return (text, count);
    }

    private static Regex BuildRegex(string query, SearchOptions options)
    {
        var pattern = options.UseRegex ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = $@"(?<!\w)(?:{pattern})(?!\w)";

        var opts = RegexOptions.Multiline;
        if (!options.CaseSensitive) opts |= RegexOptions.IgnoreCase;
        return new Regex(pattern, opts);
    }
}

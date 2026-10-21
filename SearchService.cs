using System.Text.RegularExpressions;

namespace Zypora;

public sealed record SearchOptions(bool CaseSensitive, bool WholeWord, bool UseRegex);

public static class SearchService
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(500);

    public static IReadOnlyList<(int Start, int Length)> Find(string source, string query, SearchOptions options)
    {
        var result = new List<(int, int)>();
        if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(query)) return result;

        try
        {
            var regex = BuildRegex(query, options);
            foreach (Match m in regex.Matches(source))
            {
                if (m.Length == 0) continue;
                result.Add((m.Index, m.Length));
            }
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return result;
        }
        return result;
    }

    public static (string Text, int NewCaret) ReplaceOne(
        string source,
        (int Start, int Length) match,
        string replacement,
        string? query = null,
        SearchOptions? options = null)
    {
        if (match.Start < 0 || match.Start + match.Length > source.Length)
            return (source, match.Start);

        string actual = replacement;
        if (options is { UseRegex: true } && !string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var regex = BuildRegex(query, options);
                var m = regex.Match(source, match.Start);
                if (m.Success && m.Index == match.Start && m.Length == match.Length)
                {
                    actual = m.Result(replacement);
                }
            }
            catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
            {
            }
        }

        var text = source.Remove(match.Start, match.Length).Insert(match.Start, actual);
        return (text, match.Start + actual.Length);
    }

    public static (string Text, int Count) ReplaceAll(string source, string query, string replacement, SearchOptions options)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrWhiteSpace(query)) return (source, 0);

        int count = 0;
        try
        {
            var regex = BuildRegex(query, options);
            var text = regex.Replace(source, m =>
            {
                if (m.Length == 0) return m.Value;
                count++;
                return options.UseRegex ? m.Result(replacement) : replacement;
            });
            return (text, count);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return (source, 0);
        }
    }

    private static Regex BuildRegex(string query, SearchOptions options)
    {
        var pattern = options.UseRegex ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = $@"(?<!\w)(?:{pattern})(?!\w)";

        var opts = RegexOptions.Multiline;
        if (!options.CaseSensitive) opts |= RegexOptions.IgnoreCase;
        return new Regex(pattern, opts, MatchTimeout);
    }
}

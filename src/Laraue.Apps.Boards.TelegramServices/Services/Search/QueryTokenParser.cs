using System.Text.RegularExpressions;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// One "key:value" token from the query. <see cref="IsFollowedByAnotherToken"/> is a purely
/// structural fact (another word/token comes after this one in the raw query) — it's the one
/// reliable "user has moved on" signal Telegram gives us, since trailing whitespace at the
/// very end of inline_query.query is not reliably preserved. What a filter actually does with
/// this fact (and with the raw <see cref="Value"/>, which may end in a wildcard marker) is
/// entirely up to the filter itself.
/// </summary>
public readonly record struct QueryToken(string Key, string Value, bool IsFollowedByAnotherToken);

public static class QueryTokenParser
{
    // key:value token, e.g. "org:la", "assignee:me", "upd:>7d"
    private static readonly Regex TokenRegex = new(@"^([A-Za-z]+):(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Splits the raw query into "key:value" filter tokens (matched against
    /// <paramref name="knownKeys"/>) and free-text words. The value is passed through
    /// unmodified (including any trailing wildcard marker) — filters interpret their own
    /// value, this parser only splits words and identifies which are recognized tokens.
    /// </summary>
    public static (List<QueryToken> FilterTokens, List<string> FreeTextWords) Parse(
        string rawQuery,
        IReadOnlySet<string> knownKeys)
    {
        var words = rawQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var filterTokens = new List<QueryToken>();
        var freeTextWords = new List<string>();

        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var isFollowedByAnotherToken = i < words.Length - 1;

            var match = TokenRegex.Match(word);
            if (!match.Success || !knownKeys.Contains(match.Groups[1].Value))
            {
                freeTextWords.Add(word);
                continue;
            }

            var key = match.Groups[1].Value.ToLowerInvariant();
            var value = match.Groups[2].Value;

            filterTokens.Add(new QueryToken(key, value, isFollowedByAnotherToken));
        }

        return (filterTokens, freeTextWords);
    }
}
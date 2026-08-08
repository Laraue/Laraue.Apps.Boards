using System.Text.RegularExpressions;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

public readonly record struct QueryToken(string Key, string Value, bool IsFinalized);
 
public static class QueryTokenParser
{
    // key:value token, e.g. "org:la", "assignee:me", "updated:>7d"
    private static readonly Regex TokenRegex = new(@"^([A-Za-z]+):(.*)$", RegexOptions.Compiled);
 
    /// <summary>
    /// Splits the raw query into "key:value" filter tokens (matched against
    /// <paramref name="knownKeys"/>) and free-text words. A token is "finalized" once the
    /// user has typed past it — either it isn't the last token, or the query ends in whitespace.
    /// </summary>
    public static (List<QueryToken> FilterTokens, List<string> FreeTextWords) Parse(
        string rawQuery,
        IReadOnlySet<string> knownKeys)
    {
        var endsWithSpace = rawQuery.Length > 0 && char.IsWhiteSpace(rawQuery[^1]);
        var words = rawQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
 
        var filterTokens = new List<QueryToken>();
        var freeTextWords = new List<string>();
 
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var isLastWord = i == words.Length - 1;
            var isFinalized = !isLastWord || endsWithSpace;
 
            var match = TokenRegex.Match(word);
            if (match.Success && knownKeys.Contains(match.Groups[1].Value))
            {
                filterTokens.Add(new QueryToken(match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value, isFinalized));
            }
            else
            {
                freeTextWords.Add(word);
            }
        }
 
        return (filterTokens, freeTextWords);
    }
}

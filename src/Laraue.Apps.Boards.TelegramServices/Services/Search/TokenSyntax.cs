namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>Punctuation used by the filter token syntax.</summary>
public static class TokenSyntax
{
    /// <summary>Separates the filter key from its value: <c>org:laraue</c>.</summary>
    public const char KeyValueSeparator = ':';

    /// <summary>
    /// Explicit trailing marker requesting a broader prefix/fuzzy search instead of requiring
    /// an exact match: <c>org:la*</c> matches every organization whose key starts with "la",
    /// applied immediately regardless of how many that is. Without it, a value that doesn't
    /// exactly match a candidate is treated as still being typed — shown as a picker rather
    /// than applied, since typing more could still turn it into a different, unambiguous
    /// exact match.
    /// </summary>
    public const char WildcardSuffix = '*';
}
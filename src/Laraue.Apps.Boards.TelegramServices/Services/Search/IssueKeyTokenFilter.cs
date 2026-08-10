using System.Text.RegularExpressions;
using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Handles "key:BRD-40" — an exact lookup by issue key. No candidate list to browse or
/// wildcard-search (there's no finite set of valid keys, and "starts with" doesn't make sense
/// for a unique lookup), so this filter's equivalent of "exact match" is simply "the shape is
/// complete" — LETTERS-NUMBER is inherently unambiguous once fully typed, so it applies
/// immediately with no marker needed, mirroring how org:/space:/assignee: apply immediately on
/// an exact candidate match. An incomplete-but-still-valid prefix shows a preview; a shape
/// that's already broken (can never become valid by typing more) errors immediately.
/// </summary>
public sealed class IssueKeyTokenFilter : IQueryTokenFilter
{
    private static readonly Regex StillValidPrefixRegex = new(
        @"^[A-Za-z]*(-\d*)?$", RegexOptions.Compiled);

    private static readonly Regex CompleteRegex = new(
        @"^([A-Za-z]+)-(\d+)$", RegexOptions.Compiled);

    public string Key => "key";

    public Task<TokenResolution> ResolveAsync(
        FilterContext context,
        IQueryable<Issue> query,
        string value,
        bool isFollowedByAnotherToken,
        CancellationToken ct)
    {
        if (value.Length == 0)
        {
            return Task.FromResult<TokenResolution>(new PreviewResolution(
                "Type an issue key",
                "e.g. BRD-40"));
        }

        var match = CompleteRegex.Match(value);
        if (match.Success && int.TryParse(match.Groups[2].Value, out var issueNumber))
        {
            // Uppercase the *parameter* here in .NET, not the DB column — comparing the raw
            // column to an already-uppercased constant keeps this index-friendly, unlike
            // wrapping the column itself in ToUpper()/Trim() inside the query.
            var spaceKeyUpper = match.Groups[1].Value.ToUpperInvariant();

            var filtered = query.Where(x =>
                x.Status!.Epic!.Space!.Key == spaceKeyUpper &&
                x.IssueNumber!.Number == issueNumber);

            return Task.FromResult<TokenResolution>(new AppliedResolution(
                filtered, Description: $"issue key \"{spaceKeyUpper}-{issueNumber}\""));
        }

        if (StillValidPrefixRegex.IsMatch(value))
        {
            if (isFollowedByAnotherToken)
            {
                // Moved on to another token with an incomplete key — this is a genuine
                // mistake worth flagging, not silently ignorable, since nothing more will be
                // typed into this token now.
                return Task.FromResult<TokenResolution>(new ErrorResolution(
                    "Incomplete issue key",
                    $"\"{value}\" isn't a complete issue key — expected LETTERS-NUMBER, e.g. BRD-40."));
            }

            return Task.FromResult<TokenResolution>(new PreviewResolution(
                "Type an issue key",
                $"\"{value}\" so far — keep typing, e.g. {value}...-40"));
        }

        // Already broken (e.g. a digit before any "-", or a second "-") — this can never
        // become valid by typing more characters, so error now rather than waiting.
        return Task.FromResult<TokenResolution>(new ErrorResolution(
            "Invalid issue key",
            $"\"{value}\" doesn't look like an issue key — expected LETTERS-NUMBER, e.g. BRD-40."));
    }
}
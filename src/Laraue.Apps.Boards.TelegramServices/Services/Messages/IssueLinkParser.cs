using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

/// <summary>
/// Extracts issue links out of arbitrary message text, for /info's "reply to a message that
/// mentions issues by link" mode. Recognizes two link shapes, both built by the Mini App:
/// the issue's own page ("https://boards.laraue.com/organizations/acme-a1b2/issues/BRD-185",
/// built by <see cref="IIssueUrlBuilder"/>) and the board view opening a specific issue
/// ("https://boards.laraue.com/organizations/acme-a1b2/spaces/BRD/149?issue=BRD-162"). Only
/// matches links under the configured <see cref="AppOptions.Url"/> - the same base
/// <see cref="IssueUrlBuilder"/> builds links from - so a lookalike link to a different domain
/// isn't treated as one of ours.
/// </summary>
public interface IIssueLinkParser
{
    IReadOnlyList<IssueLinkMatch> Parse(string? text);

    /// <summary>
    /// A sample link in the shape this parser actually matches (built from the same
    /// <see cref="AppOptions.Url"/> base), for showing the user what "an issue link" means when
    /// none was found.
    /// </summary>
    string ExampleLink { get; }
}

public sealed record IssueLinkMatch(string OrganizationKey, string SpaceKey, int IssueNumber);

public sealed class IssueLinkParser : IIssueLinkParser
{
    private readonly Regex _issueUrlRegex;
    private readonly Regex _boardUrlRegex;

    public IssueLinkParser(IOptions<AppOptions> options)
    {
        var baseUrl = Regex.Escape(options.Value.Url);

        // The issue's own Mini App page, e.g. ".../organizations/acme-a1b2/issues/BRD-185".
        _issueUrlRegex = new Regex(
            $@"{baseUrl}/organizations/([^/\s]+)/issues/([A-Za-z]+)-(\d+)",
            RegexOptions.Compiled);

        // The board view opening a specific issue via a query string, e.g.
        // ".../organizations/acme-a1b2/spaces/BRD/149?issue=BRD-162" - the space/board path
        // segments aren't parsed, since "issue=" already carries the space key and number, and
        // other query params (if any) may come before or after it.
        _boardUrlRegex = new Regex(
            $@"{baseUrl}/organizations/([^/\s]+)/spaces/[^\s]*[?&]issue=([A-Za-z]+)-(\d+)",
            RegexOptions.Compiled);

        ExampleLink = $"{options.Value.Url}/organizations/acme-a1b2/issues/BRD-185";
    }

    public string ExampleLink { get; }

    public IReadOnlyList<IssueLinkMatch> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<IssueLinkMatch>? matches = null;

        CollectMatches(_issueUrlRegex, text, ref matches);
        CollectMatches(_boardUrlRegex, text, ref matches);

        return matches ?? (IReadOnlyList<IssueLinkMatch>) [];
    }

    private static void CollectMatches(Regex regex, string text, ref List<IssueLinkMatch>? matches)
    {
        foreach (Match match in regex.Matches(text))
        {
            var link = new IssueLinkMatch(
                match.Groups[1].Value,
                match.Groups[2].Value.ToUpperInvariant(),
                int.Parse(match.Groups[3].Value));

            matches ??= [];
            if (!matches.Contains(link))
                matches.Add(link);
        }
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Where the Boards web app (Mini App) is served. Bound from the <c>AppOptions</c> section in every host
/// that builds links into it - the same <c>AppOptions:Url</c> TelegramHost's own, wider
/// <c>AppOptions</c> already reads, so the URL is configured once per host.
/// </summary>
public sealed class WebAppOptions
{
    [Required]
    [Url]
    public required string Url { get; set; }
}

public interface IIssueUrlBuilder
{
    /// <summary>
    /// Builds a web app link to a specific issue, e.g.
    /// "https://boards.example.com/organizations/acme-a1b2/issues/SPA-42".
    /// </summary>
    string Build(string organizationSlug, string organizationSlugPostfix, IssueKey key);
}

public class IssueUrlBuilder(IOptions<WebAppOptions> options) : IIssueUrlBuilder
{
    public string Build(string organizationSlug, string organizationSlugPostfix, IssueKey key)
    {
        var orgKey = $"{organizationSlug}-{organizationSlugPostfix}";
        return $"{options.Value.Url}/organizations/{orgKey}/issues/{key}";
    }
}

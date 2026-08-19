using Laraue.Apps.Boards.Services;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.TelegramServices;

public interface IIssueUrlBuilder
{
    /// <summary>
    /// Builds a Mini App link to a specific issue, e.g.
    /// "https://boards.example.com/organizations/acme-a1b2/issues/SPA-42".
    /// </summary>
    string Build(string organizationSlug, string organizationSlugPostfix, IssueKey key);
}

public class IssueUrlBuilder(IOptions<AppOptions> options) : IIssueUrlBuilder
{
    public string Build(string organizationSlug, string organizationSlugPostfix, IssueKey key)
    {
        var orgKey = $"{organizationSlug}-{organizationSlugPostfix}";
        return $"{options.Value.Url}/organizations/{orgKey}/issues/{key}";
    }
}

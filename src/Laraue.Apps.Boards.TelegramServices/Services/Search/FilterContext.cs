using Laraue.Apps.Boards.DataAccess;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// One readable organization, pre-fetched once per request and shared across all filters
/// so no filter needs to re-query it (org suggestions, org prefix match, assignee scoping, etc).
/// </summary>
public sealed record OrganizationInfo(long Id, string Name, string Slug);

/// <summary>
/// One readable space, pre-fetched once per request for the space: filter's prefix
/// matching/suggestions, same reasoning as <see cref="OrganizationInfo"/>.
/// </summary>
public sealed record SpaceInfo(long Id, string Key, string Name, long OrganizationId);

/// <summary>
/// Everything a token filter needs that isn't specific to the token itself.
/// Built once per inline query, then rebuilt (via a `with` copy) after any filter that narrows
/// organization or space scope (see <see cref="AppliedResolution.SelectedOrganizationIds"/> and
/// <see cref="AppliedResolution.SelectedSpaceIds"/>) — so tokens are effectively applied
/// sequentially: a later token in the same query (e.g. assignee: after org: or space:) sees
/// what an earlier one already narrowed down.
/// </summary>
public sealed record FilterContext(
    DatabaseContext DbContext,
    SearchRequest RequestContext,
    long[] ReadableSpaceIds,
    IReadOnlyList<OrganizationInfo> ReadableOrganizations,
    IReadOnlyList<SpaceInfo> ReadableSpaces,
    IReadOnlyList<long>? SelectedOrganizationIds = null,
    IReadOnlyList<long>? SelectedSpaceIds = null)
{
    /// <summary>
    /// The actual space scope currently in play, combining whatever org:/space: tokens have
    /// already narrowed with the searching user's own readable spaces. This is what any
    /// later filter (e.g. assignee:) should use when it needs to know "which spaces are we
    /// actually looking at right now" — never just <see cref="ReadableSpaceIds"/> alone, since
    /// that ignores narrowing already applied earlier in the same query.
    /// </summary>
    public IReadOnlyList<long> EffectiveSpaceIds
    {
        get
        {
            IEnumerable<long> ids = ReadableSpaceIds;

            if (SelectedSpaceIds is not null)
            {
                ids = ids.Intersect(SelectedSpaceIds);
            }
            else if (SelectedOrganizationIds is not null)
            {
                var orgScopedSpaceIds = ReadableSpaces
                    .Where(s => SelectedOrganizationIds.Contains(s.OrganizationId))
                    .Select(s => s.Id);
                ids = ids.Intersect(orgScopedSpaceIds);
            }

            return ids.ToArray();
        }
    }
}
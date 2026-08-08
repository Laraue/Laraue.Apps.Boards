using Laraue.Apps.Boards.DataAccess;

namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// One readable organization, pre-fetched once per request and shared across all filters
/// so no filter needs to re-query it (org suggestions, org prefix match, assignee scoping, etc).
/// </summary>
public sealed record OrganizationInfo(long Id, string Name, string Slug);
 
/// <summary>
/// Everything a token filter needs that isn't specific to the token itself.
/// Built once per inline query and passed to every filter's ResolveAsync call.
/// </summary>
public sealed record FilterContext(
    DatabaseContext DbContext,
    RequestContext RequestContext,
    long[] ReadableSpaceIds,
    IReadOnlyList<OrganizationInfo> ReadableOrganizations);

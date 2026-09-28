using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.History;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Extensions;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public record GetOrganizationHistoryRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public Guid? OwnerId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public required PaginationData Pagination { get; set; }
}

public record GetIssueHistoryRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public string IssueKey { get; set; } = string.Empty;
    public required PaginationData Pagination { get; set; }
}

public interface IOrganizationHistoryService
{
    Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        GetOrganizationHistoryRequest request,
        CancellationToken ct);

    Task<ShortPaginatedResult<OrganizationHistoryItem>> GetIssueHistory(
        GetIssueHistoryRequest request,
        CancellationToken ct);
}

/// <summary>
/// REST side of the history: resolves the issue/readable spaces and checks access, then reads through
/// the shared <see cref="IOrganizationHistoryReader"/>.
/// </summary>
public class OrganizationHistoryService(
    DatabaseContext context,
    IAccessService accessService,
    IOrganizationHistoryReader historyReader)
    : IOrganizationHistoryService
{
    public async Task<ShortPaginatedResult<OrganizationHistoryItem>> GetIssueHistory(
        GetIssueHistoryRequest request,
        CancellationToken ct)
    {
        var issueId = await GetIssueIdByIssueKey(
            request.AuthData.OrganizationId,
            new IssueKey(request.IssueKey),
            ct);

        // includeDeleted: true - a soft-deleted issue's own history must stay viewable to anyone
        // who could already read it.
        await accessService.GetAccessLevelsByIssueId(request.AuthData, issueId, includeDeleted: true, cancellationToken: ct)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", request.IssueKey))
            .EnsureOrThrowNotFound(a => a.CanRead, string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", request.IssueKey));

        return await historyReader.GetIssueHistory(request.AuthData.OrganizationId, issueId, request.IssueKey, request.Pagination, ct);
    }

    public async Task<ShortPaginatedResult<OrganizationHistoryItem>> GetOrganizationHistory(
        GetOrganizationHistoryRequest request,
        CancellationToken ct)
    {
        // includeDeleted: a soft-deleted space's history must stay visible to whoever could
        // already read it - the read-permission computation itself should still "see" the space.
        var readableSpaceIds = await accessService.GetAvailableSpaces(
            request.AuthData,
            query => query.Select(s => s.Id).ToArrayAsyncEF(ct),
            includeDeleted: true,
            cancellationToken: ct);

        return await historyReader.GetOrganizationHistory(
            new OrganizationHistoryQuery(
                request.AuthData.OrganizationId,
                readableSpaceIds,
                request.OwnerId,
                request.DateFrom,
                request.DateTo,
                request.Pagination),
            ct);
    }

    private Task<long> GetIssueIdByIssueKey(
        long organizationId,
        IssueKey issueKey,
        CancellationToken cancellationToken)
    {
        return context.IssueNumbers
            .Where(x => x.Number == issueKey.Number)
            .Where(x => x.Space!.Key == issueKey.SpaceKey)
            .Where(x => x.Space!.OrganizationId == organizationId)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync($"Issue: {issueKey} is not found in organization", cancellationToken);
    }
}

using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IIssueLinksService
{
    /// <summary>
    /// Links the issue to another one: "<see cref="AddIssueLinkRequest.IssueKey"/>
    /// [<see cref="AddIssueLinkRequest.Relation"/>] <see cref="AddIssueLinkRequest.OtherIssueKey"/>". Needs
    /// CanUpdateIssue on the issue and CanRead on the other one.
    /// </summary>
    Task AddLink(AddIssueLinkRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the link between two issues, whatever its type. Needs CanUpdateIssue on the issue and CanRead
    /// on the other one.
    /// </summary>
    Task RemoveLink(RemoveIssueLinkRequest request, CancellationToken cancellationToken);
}

public class IssueLinksService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreIssueLinksService coreIssueLinksService)
    : IIssueLinksService
{
    public async Task AddLink(AddIssueLinkRequest request, CancellationToken cancellationToken)
    {
        var (issueId, otherIssueId) = await GetLinkedIssueIds(
            request.AuthData,
            request.IssueKey,
            request.OtherIssueKey,
            cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssueLinksService.AddLink(
            issueId,
            request.Relation,
            otherIssueId,
            request.AuthData.UserId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RemoveLink(RemoveIssueLinkRequest request, CancellationToken cancellationToken)
    {
        var (issueId, otherIssueId) = await GetLinkedIssueIds(
            request.AuthData,
            request.IssueKey,
            request.OtherIssueKey,
            cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssueLinksService.RemoveLink(
            issueId,
            otherIssueId,
            request.AuthData.UserId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private sealed record LinkedIssueIds(long IssueId, long OtherIssueId);

    /// <summary>
    /// Resolves both keys, checking CanUpdateIssue on the issue and CanRead on the other one. An issue the
    /// caller can't read is reported as not found (404), not forbidden, so nobody learns it exists.
    /// </summary>
    private async Task<LinkedIssueIds> GetLinkedIssueIds(
        OrganizationAuthData authData,
        string issueKey,
        string otherIssueKey,
        CancellationToken cancellationToken)
    {
        var issueId = await GetIssueId(authData, issueKey, cancellationToken);
        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", issueKey))
            .EnsureOrThrowNotFound(a => a.CanRead, string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", issueKey))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", issueKey, "update"));

        var otherIssueId = await GetIssueId(authData, otherIssueKey, cancellationToken);
        await accessService.GetAccessLevelsByIssueId(authData, otherIssueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", otherIssueKey))
            .EnsureOrThrowNotFound(a => a.CanRead, string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", otherIssueKey));

        return new LinkedIssueIds(issueId, otherIssueId);
    }

    private Task<long> GetIssueId(OrganizationAuthData authData, string issueKey, CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        return context.IssueNumbers
            .Where(x => x.Number == key.Number)
            .Where(x => x.Space!.Key == key.SpaceKey)
            .Where(x => x.Space!.OrganizationId == authData.OrganizationId)
            .Where(x => x.Issue!.DeletedAt == null)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync(
                string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", issueKey),
                cancellationToken);
    }
}

public record AddIssueLinkRequest
{
    public OrganizationAuthData AuthData { get; set; }

    /// <summary>The issue the link is added from.</summary>
    public string IssueKey { get; set; } = string.Empty;

    /// <summary>What the link means from <see cref="IssueKey"/>'s side, e.g. BlockedBy.</summary>
    public required IssueLinkRelation Relation { get; init; }

    /// <summary>The issue on the other end of the link.</summary>
    public required string OtherIssueKey { get; init; }
}

public record RemoveIssueLinkRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required string IssueKey { get; init; }
    public required string OtherIssueKey { get; init; }
}

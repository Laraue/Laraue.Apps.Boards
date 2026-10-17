using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
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

    /// <summary>
    /// The issue's links to active issues the caller can read, each with its meaning from this issue's side,
    /// ordered by that meaning. No permission check on the issue itself: the caller has checked CanRead.
    /// </summary>
    Task<IssueLinkDto[]> GetLinks(OrganizationAuthData authData, long issueId, CancellationToken cancellationToken);

    /// <summary>
    /// Which of <paramref name="issueIds"/> are blocked: some active issue the caller can read blocks them
    /// and isn't in a Completed status.
    /// </summary>
    Task<HashSet<long>> GetBlockedIssueIds(
        OrganizationAuthData authData,
        IReadOnlyCollection<long> issueIds,
        CancellationToken cancellationToken);
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

    public async Task<IssueLinkDto[]> GetLinks(
        OrganizationAuthData authData,
        long issueId,
        CancellationToken cancellationToken)
    {
        var readableSpaceIds = await GetReadableSpaceIds(authData, cancellationToken);

        var outgoing = context.IssueLinks
            .Where(x => x.SourceIssueId == issueId)
            .Select(x => new { x.Type, IsSource = true, OtherIssueId = x.TargetIssueId });

        var incoming = context.IssueLinks
            .Where(x => x.TargetIssueId == issueId)
            .Select(x => new { x.Type, IsSource = false, OtherIssueId = x.SourceIssueId });

        var links = await outgoing
            .Concat(incoming)
            .Join(
                context.ActiveIssues()
                    .Where(x => readableSpaceIds.Contains(x.Status!.Epic!.SpaceId)),
                link => link.OtherIssueId,
                issue => issue.Id,
                (link, issue) => new
                {
                    link.Type,
                    link.IsSource,
                    issue.Title,
                    SpaceKey = issue.IssueNumber!.Space!.Key,
                    issue.IssueNumber.Number,
                    StatusName = issue.Status!.Epic!.IsDefault ? null : issue.Status.Name,
                    StatusColor = issue.Status.Epic.IsDefault ? null : issue.Status.Color,
                    StatusCategory = issue.Status.Category,
                })
            .ToArrayAsyncEF(cancellationToken);

        return links
            .Select(x => new IssueLinkDto
            {
                Relation = x.Type.ToRelation(x.IsSource),
                Key = new IssueKey(x.SpaceKey, x.Number).ToString(),
                Title = x.Title,
                StatusName = x.StatusName,
                StatusColor = x.StatusColor,
                StatusCategory = x.StatusCategory,
            })
            .OrderBy(x => x.Relation)
            .ThenBy(x => x.Key)
            .ToArray();
    }

    public async Task<HashSet<long>> GetBlockedIssueIds(
        OrganizationAuthData authData,
        IReadOnlyCollection<long> issueIds,
        CancellationToken cancellationToken)
    {
        if (issueIds.Count == 0)
            return [];

        var readableSpaceIds = await GetReadableSpaceIds(authData, cancellationToken);

        var blockedIssueIds = await context.IssueLinks
            .Where(x => x.Type == IssueLinkType.Blocks)
            .Where(x => issueIds.Contains(x.TargetIssueId))
            .Where(x => x.SourceIssue!.DeletedAt == null)
            .Where(x => x.SourceIssue!.Status!.Category != StatusCategory.Completed)
            .Where(x => readableSpaceIds.Contains(x.SourceIssue!.Status!.Epic!.SpaceId))
            .Select(x => x.TargetIssueId)
            .Distinct()
            .ToArrayAsyncEF(cancellationToken);

        return blockedIssueIds.ToHashSet();
    }

    private Task<long[]> GetReadableSpaceIds(OrganizationAuthData authData, CancellationToken cancellationToken)
    {
        return accessService.GetAvailableSpaces(
            authData,
            query => query
                .Select(x => x.Id)
                .ToArrayAsyncEF(cancellationToken),
            includeDeleted: false,
            cancellationToken);
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

/// <summary>
/// A link of an issue, seen from that issue's side: "this issue [<see cref="Relation"/>] <see cref="Key"/>".
/// </summary>
public record IssueLinkDto
{
    public required IssueLinkRelation Relation { get; init; }
    public required string Key { get; init; }
    public required string Title { get; init; }

    /// <summary>Null for an issue in its space's backlog, which has no visible status.</summary>
    public required string? StatusName { get; init; }
    public required string? StatusColor { get; init; }
    public required StatusCategory StatusCategory { get; init; }
}

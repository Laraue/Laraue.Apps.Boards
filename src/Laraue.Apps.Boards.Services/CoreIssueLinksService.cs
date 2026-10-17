using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Adds and removes <see cref="IssueLink"/>s. Both methods must run inside a transaction opened by the
/// caller, and check no permissions - the caller checks CanUpdateIssue on <c>issueId</c> and CanRead on
/// <c>otherIssueId</c> first. Each change is written to the history of both issues.
/// </summary>
public interface ICoreIssueLinksService
{
    /// <summary>
    /// Links two active issues of the same organization: "<paramref name="issueId"/>
    /// [<paramref name="relation"/>] <paramref name="otherIssueId"/>". Fails when the issues are already
    /// linked (whatever the type - remove that link first), or when a Blocks link would close a cycle.
    /// Returns the new link's id.
    /// </summary>
    Task<long> AddLink(
        long issueId,
        IssueLinkRelation relation,
        long otherIssueId,
        Actor actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes the link between two issues, whatever its type and direction.
    /// </summary>
    Task RemoveLink(
        long issueId,
        long otherIssueId,
        Actor actor,
        CancellationToken cancellationToken);
}

public class CoreIssueLinksService(
    DatabaseContext context,
    IIssueHistoryService historyService,
    IOrganizationLogItemFactory logItemFactory,
    IDateTimeProvider dateTimeProvider)
    : ICoreIssueLinksService
{
    public async Task<long> AddLink(
        long issueId,
        IssueLinkRelation relation,
        long otherIssueId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        if (issueId == otherIssueId)
            throw new BadRequestException(nameof(otherIssueId), "An issue can't be linked to itself.");

        var issues = await GetLinkedIssues(issueId, otherIssueId, cancellationToken);

        // Every link change of the organization runs one at a time, so two concurrent requests can't both
        // pass the checks below - neither the "one link per pair" one nor the Blocks cycle one.
        await LockOrganizationLinks(issues.OrganizationId, cancellationToken);

        var existingLink = await FindLink(issueId, otherIssueId, cancellationToken);
        if (existingLink is not null)
        {
            var existingRelation = existingLink.Type.ToRelation(existingLink.SourceIssueId == issueId);
            throw new BadRequestException(
                nameof(otherIssueId),
                $"{issues.Issue.Name} {existingRelation.ToDisplayText()} {issues.OtherIssue.Name} already. " +
                "Two issues can have only one link: remove it first to link them in another way.");
        }

        var stored = relation.ToStored(issueId, otherIssueId);

        if (stored.Type == IssueLinkType.Blocks
            && await IsBlockedTransitively(stored.SourceIssueId, stored.TargetIssueId, cancellationToken))
        {
            throw new BadRequestException(
                nameof(otherIssueId),
                $"{issues.Issue.Name} can't be {relation.ToDisplayText()} {issues.OtherIssue.Name}: " +
                "the blocking links would form a cycle.");
        }

        var now = dateTimeProvider.UtcNow;

        var link = new IssueLink
        {
            Type = stored.Type,
            SourceIssueId = stored.SourceIssueId,
            TargetIssueId = stored.TargetIssueId,
            CreatedAt = now,
            CreatedByUserId = actor.UserId,
        };

        context.IssueLinks.Add(link);
        await context.SaveChangesAsync(cancellationToken);

        await RecordOnBothIssues(
            issues,
            relation,
            actor,
            now,
            logItemFactory.IssueLinkAdded,
            cancellationToken);

        return link.Id;
    }

    public async Task RemoveLink(
        long issueId,
        long otherIssueId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var issues = await GetLinkedIssues(issueId, otherIssueId, cancellationToken);

        await LockOrganizationLinks(issues.OrganizationId, cancellationToken);

        var link = await FindLink(issueId, otherIssueId, cancellationToken)
            ?? throw new NotFoundException($"{issues.Issue.Name} and {issues.OtherIssue.Name} aren't linked.");

        var relation = link.Type.ToRelation(link.SourceIssueId == issueId);

        await context.IssueLinks
            .Where(x => x.Id == link.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await RecordOnBothIssues(
            issues,
            relation,
            actor,
            dateTimeProvider.UtcNow,
            logItemFactory.IssueLinkRemoved,
            cancellationToken);
    }

    /// <param name="Issue">The issue the change is made from: its id and key.</param>
    /// <param name="OtherIssue">The issue on the other end of the link: its id and key.</param>
    private sealed record LinkedIssues(long OrganizationId, IdName<long> Issue, IdName<long> OtherIssue);

    private async Task<LinkedIssues> GetLinkedIssues(
        long issueId,
        long otherIssueId,
        CancellationToken cancellationToken)
    {
        var issues = await context.ActiveIssues()
            .Where(x => x.Id == issueId || x.Id == otherIssueId)
            .Select(x => new
            {
                x.Id,
                x.Status!.Epic!.Space!.OrganizationId,
                SpaceKey = x.IssueNumber!.Space!.Key,
                x.IssueNumber.Number,
            })
            .ToListAsyncEF(cancellationToken);

        var issue = issues.SingleOrDefault(x => x.Id == issueId)
            ?? throw new NotFoundException($"Issue {issueId} is not found.");

        var otherIssue = issues.SingleOrDefault(x => x.Id == otherIssueId)
            ?? throw new NotFoundException($"Issue {otherIssueId} is not found.");

        var issueKey = new IdName<long>(issue.Id, new IssueKey(issue.SpaceKey, issue.Number).ToString());
        var otherIssueKey = new IdName<long>(otherIssue.Id, new IssueKey(otherIssue.SpaceKey, otherIssue.Number).ToString());

        if (issue.OrganizationId != otherIssue.OrganizationId)
            throw new BadRequestException(
                nameof(otherIssueId),
                $"{issueKey.Name} and {otherIssueKey.Name} belong to different organizations and can't be linked.");

        return new LinkedIssues(issue.OrganizationId, issueKey, otherIssueKey);
    }

    private Task LockOrganizationLinks(long organizationId, CancellationToken cancellationToken)
    {
        return context.Database.PgAdvisoryXactLock($"issue-links-{organizationId}", cancellationToken);
    }

    private Task<IssueLink?> FindLink(long issueId, long otherIssueId, CancellationToken cancellationToken)
    {
        return context.IssueLinks
            .Where(x =>
                (x.SourceIssueId == issueId && x.TargetIssueId == otherIssueId)
                || (x.SourceIssueId == otherIssueId && x.TargetIssueId == issueId))
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="blockerId"/> is already reachable from <paramref name="blockedId"/> through
    /// Blocks links, i.e. "blocker blocks blocked" would close a cycle. Soft-deleted issues are skipped:
    /// their links are hidden, so a cycle through one isn't one anybody can see.
    /// </summary>
    private async Task<bool> IsBlockedTransitively(long blockerId, long blockedId, CancellationToken cancellationToken)
    {
        // UNION (not UNION ALL) drops the issues already reached, so the walk ends even if the table holds a
        // cycle through a soft-deleted issue.
        var blocksType = (int)IssueLinkType.Blocks;

        return await context.Database
            .SqlQuery<bool>($"""
                WITH RECURSIVE blocked(issue_id) AS (
                    SELECT {blockedId}::bigint
                    UNION
                    SELECT l.target_issue_id
                    FROM issue_links l
                    JOIN blocked b ON b.issue_id = l.source_issue_id
                    JOIN issues i ON i.id = l.target_issue_id AND i.deleted_at IS NULL
                    WHERE l.type = {blocksType}
                )
                SELECT EXISTS (SELECT 1 FROM blocked WHERE issue_id = {blockerId}) AS "Value"
                """)
            .SingleAsync(cancellationToken);
    }

    private async Task RecordOnBothIssues(
        LinkedIssues issues,
        IssueLinkRelation relation,
        Actor actor,
        DateTime createdAt,
        Func<IssueLinkRelation, IdName<long>, OrganizationLogItem> createItem,
        CancellationToken cancellationToken)
    {
        // The link seen from the other issue: "A is blocked by B" is "B blocks A" there.
        var stored = relation.ToStored(issues.Issue.Id, issues.OtherIssue.Id);
        var otherRelation = stored.Type.ToRelation(stored.SourceIssueId == issues.OtherIssue.Id);

        await historyService.Record(
            issues.Issue.Id,
            LogEntityType.Issue,
            LogAction.Update,
            issues.OrganizationId,
            actor,
            createdAt,
            [createItem(relation, issues.OtherIssue)],
            cancellationToken);

        await historyService.Record(
            issues.OtherIssue.Id,
            LogEntityType.Issue,
            LogAction.Update,
            issues.OrganizationId,
            actor,
            createdAt,
            [createItem(otherRelation, issues.Issue)],
            cancellationToken);
    }
}

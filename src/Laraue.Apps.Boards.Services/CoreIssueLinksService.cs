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
/// <c>otherIssueId</c> first. Each change is written to the history of both issues - built here rather than
/// through <see cref="IIssueHistoryService"/>, which saves one row per call, so a change's rows (or a whole
/// batch of them) are saved together.
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

    /// <summary>
    /// Removes the links between issues of <paramref name="organizationId"/> and issues of other organizations,
    /// which a link must never join. Call it after a space or epic moved out of <paramref name="organizationId"/>:
    /// that is what leaves such links behind (links among the moved issues themselves stay). Each removal is
    /// written to the history of both issues, like <see cref="RemoveLink"/>.
    /// </summary>
    Task RemoveCrossOrganizationLinks(
        long organizationId,
        Actor actor,
        CancellationToken cancellationToken);
}

public class CoreIssueLinksService(
    DatabaseContext context,
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

        // Every link change of the organization runs one at a time, so two concurrent requests can't both
        // pass the checks below - neither the "one link per pair" one nor the Blocks cycle one.
        var issues = await LockAndGetLinkedIssues(issueId, otherIssueId, cancellationToken);

        var existingLink = await FindLink(issueId, otherIssueId, cancellationToken);
        if (existingLink is not null)
            throw new BadRequestException(
                nameof(otherIssueId),
                $"{issues.Issue.Name} and {issues.OtherIssue.Name} are linked already. " +
                "Two issues can have only one link: remove it first to link them in another way.");

        var stored = relation.ToStored(issueId, otherIssueId);

        if (stored.Type == IssueLinkType.Blocks
            && await IsBlockedTransitively(stored.SourceIssueId, stored.TargetIssueId, cancellationToken))
        {
            var (blocker, blocked) = stored.SourceIssueId == issueId
                ? (issues.Issue.Name, issues.OtherIssue.Name)
                : (issues.OtherIssue.Name, issues.Issue.Name);

            throw new BadRequestException(
                nameof(otherIssueId),
                $"{blocker} can't block {blocked}: {blocked} already blocks {blocker}, directly or through other " +
                "issues, and the blocking links would form a cycle.");
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

        context.OrganizationLogs.AddRange(CreateLogsForBothIssues(
            new LinkSide(issues.OrganizationId, issues.Issue),
            relation,
            new LinkSide(issues.OrganizationId, issues.OtherIssue),
            actor,
            now,
            logItemFactory.IssueLinkAdded));
        await context.SaveChangesAsync(cancellationToken);

        return link.Id;
    }

    public async Task RemoveLink(
        long issueId,
        long otherIssueId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var issues = await LockAndGetLinkedIssues(issueId, otherIssueId, cancellationToken);

        var link = await FindLink(issueId, otherIssueId, cancellationToken)
            ?? throw new NotFoundException($"{issues.Issue.Name} and {issues.OtherIssue.Name} aren't linked.");

        var relation = link.Type.ToRelation(link.SourceIssueId == issueId);

        await context.IssueLinks
            .Where(x => x.Id == link.Id)
            .ExecuteDeleteAsync(cancellationToken);

        context.OrganizationLogs.AddRange(CreateLogsForBothIssues(
            new LinkSide(issues.OrganizationId, issues.Issue),
            relation,
            new LinkSide(issues.OrganizationId, issues.OtherIssue),
            actor,
            dateTimeProvider.UtcNow,
            logItemFactory.IssueLinkRemoved));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveCrossOrganizationLinks(
        long organizationId,
        Actor actor,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        await context.Database.LockOrganizationIssueLinks(organizationId, cancellationToken);

        // The moved issues are in their new organization already, so the links to remove are the ones with one
        // issue still in organizationId and the other one elsewhere. Raw issues: a soft-deleted issue's links
        // are only hidden, they must not cross organizations either.
        var crossingLinks = context.IssueLinks
            .Select(x => new
            {
                x.Id,
                x.Type,
                Source = new
                {
                    IssueId = x.SourceIssueId,
                    x.SourceIssue!.Status!.Epic!.Space!.OrganizationId,
                    SpaceKey = x.SourceIssue.IssueNumber!.Space!.Key,
                    x.SourceIssue.IssueNumber.Number,
                },
                Target = new
                {
                    IssueId = x.TargetIssueId,
                    x.TargetIssue!.Status!.Epic!.Space!.OrganizationId,
                    SpaceKey = x.TargetIssue.IssueNumber!.Space!.Key,
                    x.TargetIssue.IssueNumber.Number,
                },
            })
            .Where(x =>
                (x.Source.OrganizationId == organizationId && x.Target.OrganizationId != organizationId)
                || (x.Source.OrganizationId != organizationId && x.Target.OrganizationId == organizationId));

        var now = dateTimeProvider.UtcNow;

        // A batch at a time: each one is deleted before the next is read, so the same query returns the next
        // batch. Its history rows are saved together and detached afterwards, so neither memory nor the change
        // tracker grows with the number of links.
        while (true)
        {
            var links = await crossingLinks
                .OrderBy(x => x.Id)
                .Take(CrossOrganizationLinksBatchSize)
                .ToListAsyncEF(cancellationToken);

            if (links.Count == 0)
                return;

            var linkIds = links.Select(x => x.Id).ToArray();
            await context.IssueLinks
                .Where(x => linkIds.Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);

            var logs = links
                .SelectMany(link => CreateLogsForBothIssues(
                    new LinkSide(
                        link.Source.OrganizationId,
                        new IdName<long>(link.Source.IssueId, new IssueKey(link.Source.SpaceKey, link.Source.Number).ToString())),
                    link.Type.ToRelation(isSource: true),
                    new LinkSide(
                        link.Target.OrganizationId,
                        new IdName<long>(link.Target.IssueId, new IssueKey(link.Target.SpaceKey, link.Target.Number).ToString())),
                    actor,
                    now,
                    logItemFactory.IssueLinkRemoved))
                .ToArray();

            context.OrganizationLogs.AddRange(logs);
            await context.SaveChangesAsync(cancellationToken);

            foreach (var log in logs)
            {
                foreach (var item in log.Items!)
                    context.Entry(item).State = EntityState.Detached;

                context.Entry(log).State = EntityState.Detached;
            }

            if (links.Count < CrossOrganizationLinksBatchSize)
                return;
        }
    }

    private const int CrossOrganizationLinksBatchSize = 500;

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

    /// <summary>
    /// Locks the link changes of the issues' organization and reads the issues again after that: a space
    /// move that held the lock may have moved them to another organization meanwhile. Then the request
    /// fails rather than taking that organization's lock too - holding two organizations' locks, in whatever
    /// order the moves dictate, is how two requests would deadlock.
    /// </summary>
    private async Task<LinkedIssues> LockAndGetLinkedIssues(
        long issueId,
        long otherIssueId,
        CancellationToken cancellationToken)
    {
        var lockedOrganizationId = (await GetLinkedIssues(issueId, otherIssueId, cancellationToken)).OrganizationId;

        await context.Database.LockOrganizationIssueLinks(lockedOrganizationId, cancellationToken);

        var issues = await GetLinkedIssues(issueId, otherIssueId, cancellationToken);
        if (issues.OrganizationId != lockedOrganizationId)
            throw new BadRequestException(
                nameof(issueId),
                $"{issues.Issue.Name} was just moved to another organization. Try again.");

        return issues;
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

    /// <summary>One end of a link: the issue's id and key, and the organization its history is written to.</summary>
    private sealed record LinkSide(long OrganizationId, IdName<long> Issue);

    /// <summary>
    /// The history rows of "<paramref name="issue"/> [<paramref name="relation"/>] <paramref name="otherIssue"/>"
    /// for both issues, each from its own side - not added to the context yet.
    /// </summary>
    private static OrganizationLog[] CreateLogsForBothIssues(
        LinkSide issue,
        IssueLinkRelation relation,
        LinkSide otherIssue,
        Actor actor,
        DateTime createdAt,
        Func<IssueLinkRelation, IdName<long>, OrganizationLogItem> createItem)
    {
        // The link seen from the other issue: "A is blocked by B" is "B blocks A" there.
        var stored = relation.ToStored(issue.Issue.Id, otherIssue.Issue.Id);
        var otherRelation = stored.Type.ToRelation(stored.SourceIssueId == otherIssue.Issue.Id);

        return
        [
            CreateLog(issue, createItem(relation, otherIssue.Issue), actor, createdAt),
            CreateLog(otherIssue, createItem(otherRelation, issue.Issue), actor, createdAt),
        ];
    }

    private static OrganizationLog CreateLog(LinkSide side, OrganizationLogItem item, Actor actor, DateTime createdAt)
    {
        return new OrganizationLog
        {
            EntityId = side.Issue.Id,
            EntityType = LogEntityType.Issue,
            Action = LogAction.Update,
            OrganizationId = side.OrganizationId,
            OwnerId = actor.UserId,
            ApiKeyId = actor.ApiKeyId,
            CreatedAt = createdAt,
            Items = [item],
        };
    }
}

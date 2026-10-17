using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Core.DataAccess.EFCore.Extensions;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Links;

/// <summary>
/// Issue link reads shared by the hosts that show links (WebApiHost, McpHost). A plain read with <b>no
/// permission checks</b>: the caller checks access to the issue and passes the spaces it can read, and only
/// active issues in those spaces are shown on the other end of a link.
/// </summary>
public interface IIssueLinksReader
{
    /// <summary>
    /// The issue's links, each with its meaning from this issue's side, ordered by that meaning.
    /// </summary>
    Task<IssueLinkDto[]> GetLinks(
        long issueId,
        IReadOnlyCollection<long> readableSpaceIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Which of <paramref name="issueIds"/> are blocked: an active issue in <paramref name="readableSpaceIds"/>
    /// blocks them and isn't in a Completed status.
    /// </summary>
    Task<HashSet<long>> GetBlockedIssueIds(
        IReadOnlyCollection<long> issueIds,
        IReadOnlyCollection<long> readableSpaceIds,
        CancellationToken cancellationToken);
}

public class IssueLinksReader(DatabaseContext context) : IIssueLinksReader
{
    public async Task<IssueLinkDto[]> GetLinks(
        long issueId,
        IReadOnlyCollection<long> readableSpaceIds,
        CancellationToken cancellationToken)
    {
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
        IReadOnlyCollection<long> issueIds,
        IReadOnlyCollection<long> readableSpaceIds,
        CancellationToken cancellationToken)
    {
        if (issueIds.Count == 0)
            return [];

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

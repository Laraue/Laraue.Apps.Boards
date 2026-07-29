using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Sorting;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.Services;

public interface ICoreIssuesService
{
    Task<long> Create(
        Guid ownerId,
        Guid? assigneeId,
        string? text,
        DateTime createdAt,
        long statusId,
        long? telegramMessageId,
        IEnumerable<MediaInfo> newFiles,
        CancellationToken cancellationToken);
    
    Task Update(
        long issueId,
        Guid updaterId,
        Action<UpdateSettersBuilder<Issue>> setters,
        IEnumerable<MediaInfo> newFiles,
        IEnumerable<Guid> deleteAttachmentIds,
        CancellationToken cancellationToken);
    
    Task UpdateAttributes(
        long issueId,
        UpdateIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken);
    
    Task Delete(
        long id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Add issue comment.
    /// </summary>
    Task<long> AddComment(
        long issueId,
        Guid ownerId,
        string comment,
        IEnumerable<MediaInfo> mediaInfos,
        CancellationToken cancellationToken);
    
    Task UpdateComment(
        long commentId,
        Guid ownerId,
        string comment,
        IEnumerable<MediaInfo> newFiles,
        IEnumerable<Guid> deleteAttachmentIds,
        CancellationToken cancellationToken);
    
    Task DeleteComment(
        long id,
        CancellationToken cancellationToken);

    Task UpdateIssuesOrder(
        long[] issueIds,
        long targetIssueId,
        OrderTargetType targetType,
        CancellationToken ct);
    
    /// <summary>
    /// Move issue to new status.
    /// </summary>
    Task UpdateIssuesStatus(
        long[] issueIds,
        long statusId,
        CancellationToken ct);
}

public class CoreIssuesService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    ISpaceCounterService spaceCounterService,
    IOrganizationConcurrencyControlService organizationConcurrencyControlService,
    IIssueNumbersService issueNumbersService)
    : ICoreIssuesService
{
    public async Task<long> Create(
        Guid ownerId,
        Guid? assigneeId,
        string? text,
        DateTime createdAt,
        long statusId,
        long? telegramMessageId,
        IEnumerable<MediaInfo> newFiles,
        CancellationToken cancellationToken)
    {
        var issueData = await context.Statuses
            .Where(x => x.Id == statusId)
            .Select(x => new { x.Epic!.SpaceId, x.Epic.Space!.OrganizationId, x.EpicId })
            .FirstOrThrowNotFoundEFAsync("Space was not found", cancellationToken);
        
        LexoRank? issueLexoRank = null;
        await organizationConcurrencyControlService.ExecuteIssueRankRelatedOperation(
            issueData.OrganizationId,
            async () =>
            {
                var lastLexoRankString = await context.Issues
                    .Where(x => x.Status!.Epic!.Space!.OrganizationId == issueData.OrganizationId)
                    .OrderByDescending(x => x.LexoRank)
                    .Select(x => x.LexoRank)
                    .FirstOrDefaultAsync(cancellationToken);
                
                LexoRank.TryParse(lastLexoRankString, out var lastLexoRank);
                issueLexoRank = lastLexoRank is null ? LexoRank.Middle() : lastLexoRank.GenNext();
            },
            cancellationToken);

        if (issueLexoRank == null)
            throw new InvalidOperationException("Lexo rank should be set here");
        
        var issue = new Issue
        {
            Content = text,
            OwnerId = ownerId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            TelegramMessageId = telegramMessageId,
            StatusId = statusId,
            AssigneeId = assigneeId ?? ownerId,
            LexoRank = issueLexoRank.ToString(),
        };
        
        var issueNumber = new IssueNumber
        {
            Number = await spaceCounterService.GetNextNumber(issueData.SpaceId, cancellationToken),
            Issue = issue,
            SpaceId = issueData.SpaceId,
        };
        
        context.Add(issue);
        context.Add(issueNumber);
        
        await context.SaveChangesAsync(cancellationToken);

        await AttachIssueFiles(issue.Id, ownerId, newFiles, cancellationToken);
        await TouchEpics([issueData.EpicId], createdAt, cancellationToken);
        
        return issue.Id;
    }

    public async Task Update(
        long issueId,
        Guid updaterId,
        Action<UpdateSettersBuilder<Issue>> setters,
        IEnumerable<MediaInfo> newFiles,
        IEnumerable<Guid> deleteAttachmentIds,
        CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;

        var epicData = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new { x.Status!.EpicId })
            .FirstAsyncEF(cancellationToken);

        await context.Issues
            .Where(x => x.Id == issueId)
            .ExecuteUpdateAsync(
                upd =>
                {
                    setters(upd);
                    upd.SetProperty(x => x.UpdatedAt, date);
                },
                cancellationToken);
        
        await TouchEpics([epicData.EpicId], date, cancellationToken);
        await AttachIssueFiles(issueId, updaterId, newFiles, cancellationToken);
        await DetachIssueAttachments(issueId, deleteAttachmentIds, cancellationToken);
    }

    public async Task UpdateAttributes(
        long issueId,
        UpdateIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        await UpdateTextAttributes(
            issueId,
            attributeRequests.OfType<UpdateIssueTextAttributeRequest>().ToArray(),
            cancellationToken);
        
        await UpdateListAttributes(
            issueId,
            attributeRequests.OfType<UpdateIssueListAttributeRequest>().ToArray(),
            cancellationToken);
    }
    
    private async Task UpdateListAttributes(
        long issueId,
        UpdateIssueListAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        var oldAttributes = (await context.IssueAttributeListValues
            .Where(x => x.IssueId == issueId)
            .ToArrayAsyncEF(cancellationToken))
            .ToDictionary(x => x.AttributeId);

        if (attributeRequests.Any())
        {
            foreach (var request in attributeRequests)
            {
                // Update old
                if (oldAttributes.TryGetValue(request.Id, out var oldAttribute))
                {
                    oldAttribute.AttributeListValueId = request.Value;
                    context.Entry(oldAttribute).State = EntityState.Modified;
                }
                // Insert new
                else
                {
                    context.Add(new IssueAttributeListValue
                    {
                        AttributeId = request.Id,
                        IssueId = issueId,
                        AttributeListValueId = request.Value,
                    });
                }
            }
            
            await context.SaveChangesAsync(cancellationToken);
        }
        
        // Drop old
        var toDelete = oldAttributes.Keys
            .Except(attributeRequests.Select(x => x.Id))
            .ToArray();

        if (toDelete.Length != 0)
            await context.IssueAttributeListValues
                .Where(x => x.IssueId == issueId)
                .Where(x => ((IEnumerable<long>)toDelete).Contains(x.AttributeId))
                .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task UpdateTextAttributes(
        long issueId,
        UpdateIssueTextAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        var oldAttributes = (await context.IssueAttributeTextValues
            .Where(x => x.IssueId == issueId)
            .ToArrayAsyncEF(cancellationToken))
            .ToDictionary(x => x.AttributeId);

        if (attributeRequests.Any())
        {
            foreach (var request in attributeRequests)
            {
                // Update old
                if (oldAttributes.TryGetValue(request.Id, out var oldAttribute))
                {
                    oldAttribute.Text = request.Value;
                    context.Entry(oldAttribute).State = EntityState.Modified;
                }
                // Insert new
                else
                {
                    context.Add(new IssueAttributeTextValue
                    {
                        AttributeId = request.Id,
                        IssueId = issueId,
                        Text = request.Value,
                    });
                }
            }
            
            await context.SaveChangesAsync(cancellationToken);
        }
        
        // Drop old
        var toDelete = oldAttributes.Keys
            .Except(attributeRequests.Select(x => x.Id))
            .ToArray();

        if (toDelete.Length != 0)
            await context.IssueAttributeTextValues
                .Where(x => x.IssueId == issueId)
                .Where(x => ((IEnumerable<long>)toDelete).Contains(x.AttributeId))
                .ExecuteDeleteAsync(cancellationToken);
    }

    public Task Delete(long id, CancellationToken cancellationToken)
    {
        return context.Issues
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<long> AddComment(
        long issueId,
        Guid ownerId,
        string comment,
        IEnumerable<MediaInfo> mediaInfos,
        CancellationToken cancellationToken)
    {
        var issueComment = new IssueComment
        {
            Text = comment,
            IssueId = issueId,
            OwnerId = ownerId,
            CreatedAt = dateTimeProvider.UtcNow,
            UpdatedAt = dateTimeProvider.UtcNow,
        };

        context.Add(issueComment);
        
        foreach (var mediaInfo in mediaInfos)
        {
            var attachment = new IssueCommentAttachment
            {
                Comment = issueComment,
                Attachment = GetAttachmentEntity(ownerId, mediaInfo),
            };
        
            context.Add(attachment);
        }
        
        await context.SaveChangesAsync(cancellationToken);
        return issueComment.Id;
    }

    public async Task UpdateComment(
        long commentId,
        Guid ownerId,
        string comment,
        IEnumerable<MediaInfo> newFiles,
        IEnumerable<Guid> deleteAttachmentIds,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        await context.IssueComments
            .Where(x => x.Id == commentId)
            .ExecuteUpdateAsync(u => u
                .SetProperty(p => p.Text, _ => comment),
                cancellationToken);
        
        foreach (var mediaInfo in newFiles)
        {
            var attachment = new IssueCommentAttachment
            {
                CommentId = commentId,
                Attachment = GetAttachmentEntity(ownerId, mediaInfo),
            };
        
            context.Add(attachment);
        }
        
        await context.SaveChangesAsync(cancellationToken);
        await context.IssueCommentsAttachments
            .Where(x => x.CommentId == commentId)
            .Where(x => deleteAttachmentIds.Contains(x.AttachmentId))
            .Select(x => x.Attachment)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteComment(long id, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        await context.IssueCommentsAttachments
            .Where(x => x.CommentId == id)
            .Select(x => x.Attachment)
            .ExecuteDeleteAsync(cancellationToken);
        
        await context.IssueComments
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task UpdateIssuesOrder(
        long[] issueIds,
        long targetIssueId,
        OrderTargetType targetType,
        CancellationToken ct)
    {
        var organizationData = await context.Issues
            .Where(x => x.Id == targetIssueId)
            .Select(x => new { x.Status!.Epic!.Space!.OrganizationId })
            .FirstAsyncEF(ct);

        var organizationId = organizationData.OrganizationId;
        await organizationConcurrencyControlService.ExecuteIssueRankRelatedOperation(
            organizationId,
            () => ChangesIssuesOrderInternal(organizationId, issueIds, targetIssueId, targetType, ct),
            ct);
    }
    
    public async Task UpdateIssuesStatus(
        long[] issueIds,
        long statusId,
        CancellationToken ct)
    {
        context.Database.EnsureTransactionStarted();
        
        var oldIssuesData = await context.Issues
            .Where(i => ((IEnumerable<long>)issueIds).Contains(i.Id))
            .Select(i => new
            {
                i.Id,
                i.Status!.Epic!.SpaceId,
                i.Status!.Epic!.Space!.OrganizationId,
            })
            .ToListAsyncEF(ct);
        
        var newSpaceData = await context.Statuses
            .Where(i => i.Id == statusId)
            .Select(i => new { i.Epic!.SpaceId, i.Epic!.Space!.OrganizationId })
            .FirstOrThrowNotFoundEFAsync($"Status: {statusId} is not found", ct);

        // TODO - If organization can be changed, is it possible to pass issues from different orgs? Or we will leave that limit?
        if (oldIssuesData.Any(x => x.OrganizationId != newSpaceData.OrganizationId))
            throw new InvalidOperationException("Change issue status works only inside the organization");
        
        await context.Issues
            .Where(i => ((IEnumerable<long>)issueIds).Contains(i.Id))
            .ExecuteUpdateAsync(
                upd =>
                {
                    upd
                        .SetProperty(x => x.StatusId, statusId)
                        .SetProperty(x => x.UpdatedAt, dateTimeProvider.UtcNow);
                },
                ct);

        var issuesWithUpdatedSpace = oldIssuesData
            .Where(i => i.SpaceId != newSpaceData.SpaceId)
            .GroupBy(i => i.SpaceId)
            .ToArray();

        if (issuesWithUpdatedSpace.Length == 0)
            return;

        foreach (var issues in issuesWithUpdatedSpace)
        {
            var affectedIssueNumbers = context.IssueNumbers
                .Where(i => issues.Select(x => x.Id).Contains(i.IssueId));
            
            await issueNumbersService.UpdateIssueNumbers(affectedIssueNumbers, issues.Key, ct);
        }
    }

    private async Task ChangesIssuesOrderInternal(
        long organizationId,
        long[] issueIds,
        long targetIssueId,
        OrderTargetType targetType,
        CancellationToken ct)
    {
        var allIds = issueIds
            .Concat([targetIssueId])
            .ToArray();
        
        var targetRank = await context.Issues
            .Where(x => x.Id == targetIssueId)
            .Select(x => x.LexoRank)
            .FirstOrDefaultAsyncEF(ct);
        
        var closestRank = await context.Issues
            .Where(x => x.Status!.Epic!.Space!.OrganizationId == organizationId)
            .Where(x => !((IEnumerable<long>)allIds).Contains(x.Id))
            .Where(x => targetType == OrderTargetType.After
                ? x.LexoRank.CompareTo(targetRank) > 0
                : x.LexoRank.CompareTo(targetRank) < 0)
            .ApplySorting(
                x => x.LexoRank,
                targetType == OrderTargetType.After ? SortingDirection.Ascending : SortingDirection.Descending)
            .Select(x => x.LexoRank)
            .FirstOrDefaultAsync(ct);
        
        var targetLexoRank = targetRank is not null ? LexoRank.Parse(targetRank) : null;
        var closestLexoRank = closestRank is not null ? LexoRank.Parse(closestRank) : null;

        var (previous, next) = targetType == OrderTargetType.After
            ? (targetLexoRank, closestLexoRank)
            : (closestLexoRank, targetLexoRank);
        
        foreach (var issueId in issueIds)
        {
            var newRank = LexoRank.Between(previous, next);
            previous = newRank;

            var entity = new Issue
            {
                Id = issueId,
                LexoRank = newRank.ToString(),
            };
            
            context.Attach(entity);
            context.Entry(entity).Property(x => x.LexoRank).IsModified = true;
        }
        
        await context.SaveChangesAsync(ct);
    }

    private Task AttachIssueFiles(
        long issueId,
        Guid ownerId,
        IEnumerable<MediaInfo> mediaInfos,
        CancellationToken cancellationToken)
    {
        foreach (var mediaInfo in mediaInfos)
        {
            var attachment = new IssueAttachment
            {
                IssueId = issueId,
                Attachment = GetAttachmentEntity(ownerId, mediaInfo),
            };
        
            context.Add(attachment);
        }
        
        return context.SaveChangesAsync(cancellationToken);
    }

    private Task DetachIssueAttachments(long issueId, IEnumerable<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        return context.IssueAttachments
            .Where(x => x.IssueId == issueId)
            .Where(x => attachmentIds.Contains(x.AttachmentId))
            .Select(x => x.Attachment)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private Attachment GetAttachmentEntity(Guid ownerId, MediaInfo mediaInfo)
    {
        return new Attachment
        {
            CreatedAt = dateTimeProvider.UtcNow,
            OwnerId = ownerId,
            PreviewFileId = mediaInfo.PreviewFileId,
            FileId = mediaInfo.OriginalFileId,
            Type = mediaInfo.Type,
        };
    }

    private Task<int> TouchEpics(long[] epicIds, DateTime touchedAt, CancellationToken ct)
    {
        return context.Epics
            .Where(x => ((IEnumerable<long>)epicIds).Contains(x.Id))
            .ExecuteUpdateAsync(x => x
                .SetProperty(
                    p => p.TouchedAt,
                    old => old!.TouchedAt > touchedAt ? old.TouchedAt : touchedAt),
                ct);
    }
}

public abstract record UpdateIssueAttributeRequest
{
    public long Id { get; set; }
}

public record UpdateIssueTextAttributeRequest : UpdateIssueAttributeRequest
{
    public required string Value { get; set; }
}

public record UpdateIssueListAttributeRequest : UpdateIssueAttributeRequest
{
    public required long Value { get; set; }
}

public enum OrderTargetType
{
    After,
    Before,
}

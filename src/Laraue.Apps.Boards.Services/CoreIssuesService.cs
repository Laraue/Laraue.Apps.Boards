using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Sorting;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

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
        SetIssueAttributeRequest[] attributes,
        MediaInfo[] newFiles,
        CancellationToken cancellationToken);
    
    Task Update(
        long issueId,
        Guid updaterId,
        Action<UpdateSettersBuilder<Issue>> setters,
        SetIssueAttributeRequest[] attributes,
        MediaInfo[] newFiles,
        Guid[] deleteAttachmentIds,
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
        SetIssueAttributeRequest[] attributes,
        MediaInfo[] newFiles,
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
        
        await UpdateAttributes(issue.Id, issueData.OrganizationId, attributes, cancellationToken);
        await AttachIssueFiles(issue.Id, ownerId, newFiles, cancellationToken);
        await TouchEpics([issueData.EpicId], createdAt, cancellationToken);
        
        return issue.Id;
    }

    public async Task Update(
        long issueId,
        Guid updaterId,
        Action<UpdateSettersBuilder<Issue>> setters,
        SetIssueAttributeRequest[] attributes,
        MediaInfo[] newFiles,
        Guid[] deleteAttachmentIds,
        CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;

        var epicData = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new { x.Status!.EpicId, x.Status.Epic!.Space!.OrganizationId })
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

        var change = new IssueUpdate
        {
            CreatedAt = date,
            IssueId = issueId,
            Items = []
        };
        
        change.Items.AddRange(await AttachIssueFiles(issueId, updaterId, newFiles, cancellationToken));
        change.Items.AddRange(await DetachIssueAttachments(issueId, deleteAttachmentIds, cancellationToken));
        change.Items.AddRange(await UpdateAttributes(issueId, epicData.OrganizationId, attributes, cancellationToken));

        context.Add(change);
        await context.SaveChangesAsync(cancellationToken);
        
        await TouchEpics([epicData.EpicId], date, cancellationToken);
    }

    private async Task<IssueUpdateItem[]> UpdateAttributes(
        long issueId,
        long organizationId,
        SetIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var changes = new List<IssueUpdateItem>();

        var attributeNameById = await context.Attributes
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsyncEF(x => x.Id, x => x.Name, cancellationToken);
        
        changes.AddRange(
            await UpdateTextAttributes(
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueTextAttributeRequest>().ToArray(),
                cancellationToken));
        
        changes.AddRange(
            await UpdateListAttributes(
                issueId,
                attributeNameById,
                attributeRequests.OfType<SetIssueListAttributeRequest>().ToArray(),
                cancellationToken));

        return changes.ToArray();
    }
    
    private async Task<IssueUpdateItem[]> UpdateListAttributes(
        long issueId,
        Dictionary<long, string> attributeNameById,
        SetIssueListAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        var oldAttributes = await context.IssueAttributeListValues
            .Where(x => x.IssueId == issueId)
            .Select(x => new
            {
                x.Id,
                x.AttributeId,
                x.AttributeListValueId,
                AttributeListValue = x.AttributeListValue!.Value,
            })
            .ToArrayAsyncEF(cancellationToken);
            
        var oldAttributeById =  oldAttributes
            .ToDictionary(x => x.AttributeId);

        var changes = new List<IssueUpdateItem>();

        if (attributeRequests.Length > 0)
        {
            var valueNames = await context.AttributeListValues
                .Where(x => attributeRequests.Select(y => y.Id).Contains(x.AttributeId))
                .Select(x => new { x.Id, x.AttributeId, x.Value })
                .ToArrayAsyncEF(cancellationToken);
        
            var valueNamesByAttributeId = valueNames
                .GroupBy(x => x.AttributeId)
                .ToDictionary(
                    x => x.Key,
                    x => x.ToDictionary(
                        y => y.Id,
                        y => y.Value));
            
            foreach (var request in attributeRequests)
            {
                // Update old
                if (oldAttributeById.TryGetValue(request.Id, out var oldAttribute))
                {
                    var entity = new IssueAttributeListValue
                    {
                        Id = oldAttribute.Id,
                        IssueId = issueId,
                        AttributeId = oldAttribute.AttributeId,
                        AttributeListValueId = request.ListValueId,
                    };

                    context.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;
                    
                    changes.Add(new IssueUpdateItem
                    {
                        NewDisplayValue = valueNamesByAttributeId[request.Id][request.ListValueId],
                        OldDisplayValue = oldAttribute.AttributeListValue,
                        EntityType = IssueUpdateEntityType.Property,
                        Action = ChangeAction.Update,
                        OldValueId = oldAttribute.AttributeListValueId.ToString(),
                        NewValueId = request.ListValueId.ToString(),
                        PropertyName = attributeNameById[request.Id],
                    });
                }
                // Insert new
                else
                {
                    context.Add(new IssueAttributeListValue
                    {
                        AttributeId = request.Id,
                        IssueId = issueId,
                        AttributeListValueId = request.ListValueId,
                    });
                    
                    changes.Add(new IssueUpdateItem
                    {
                        NewDisplayValue = valueNamesByAttributeId[request.Id][request.ListValueId],
                        EntityType = IssueUpdateEntityType.Property,
                        Action = ChangeAction.Create,
                        NewValueId = request.ListValueId.ToString(),
                        PropertyName = attributeNameById[request.Id],
                    });
                }
            }
            
            await context.SaveChangesAsync(cancellationToken);
        }
        
        // Drop old
        var toDelete = oldAttributeById.Keys
            .Except(attributeRequests.Select(x => x.Id))
            .ToArray();

        if (toDelete.Length != 0)
        {
            var deletableValues = await context.IssueAttributeListValues
                .Where(x => x.IssueId == issueId)
                .Where(x => ((IEnumerable<long>)toDelete).Contains(x.AttributeId))
                .Select(x => new
                {
                    x.Id,
                    AttributeListValueName = x.AttributeListValue!.Value,
                    x.AttributeListValueId,
                })
                .ToDictionaryAsyncEF(x => x.Id, cancellationToken);
            
            foreach (var deletableValue in deletableValues)
            {
                changes.Add(new IssueUpdateItem
                {
                    OldDisplayValue = deletableValue.Value.AttributeListValueName,
                    EntityType = IssueUpdateEntityType.Property,
                    Action = ChangeAction.Delete,
                    OldValueId = deletableValue.Value.AttributeListValueId.ToString(),
                    PropertyName = attributeNameById[deletableValue.Key],
                });
            }

            await context.IssueAttributeListValues
                .Where(x => deletableValues.Select(v => v.Key).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return changes.ToArray();
    }

    private async Task<IssueUpdateItem[]> UpdateTextAttributes(
        long issueId,
        Dictionary<long, string> attributeNameById,
        SetIssueTextAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        var oldAttributes = (await context.IssueAttributeTextValues
            .Where(x => x.IssueId == issueId)
            .Select(x => new { x.AttributeId, x.Text })
            .ToArrayAsyncEF(cancellationToken))
            .ToDictionary(x => x.AttributeId);

        var changes = new List<IssueUpdateItem>();
        
        if (attributeRequests.Any())
        {
            foreach (var request in attributeRequests)
            {
                // Update old
                if (oldAttributes.TryGetValue(request.Id, out var oldAttribute) && oldAttribute.Text != request.Value)
                {
                    var entity = new IssueAttributeTextValue
                    {
                        IssueId = issueId,
                        AttributeId = request.Id,
                        Text = request.Value,
                    };
                    
                    context.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;
                    
                    changes.Add(new IssueUpdateItem
                    {
                        NewDisplayValue = request.Value,
                        OldDisplayValue = oldAttribute.Text,
                        EntityType = IssueUpdateEntityType.Property,
                        Action = ChangeAction.Update,
                        PropertyName = attributeNameById[oldAttribute.AttributeId],
                    });
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
                    
                    changes.Add(new IssueUpdateItem
                    {
                        NewDisplayValue = request.Value,
                        EntityType = IssueUpdateEntityType.Property,
                        Action = ChangeAction.Create,
                        PropertyName = attributeNameById[request.Id],
                    });
                }
            }
            
            await context.SaveChangesAsync(cancellationToken);
        }
        
        // Drop old
        var toDelete = oldAttributes
            .ExceptBy(attributeRequests.Select(x => x.Id), x => x.Key)
            .ToArray();

        if (toDelete.Length != 0)
            await context.IssueAttributeTextValues
                .Where(x => x.IssueId == issueId)
                .Where(x => toDelete.Select(y => y.Key).Contains(x.AttributeId))
                .ExecuteDeleteAsync(cancellationToken);

        foreach (var deletable in toDelete)
        {
            changes.Add(new IssueUpdateItem
            {
                OldDisplayValue = deletable.Value.Text,
                EntityType = IssueUpdateEntityType.Property,
                Action = ChangeAction.Delete,
                PropertyName = attributeNameById[deletable.Key],
            });
        }
        
        return changes.ToArray();
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

    private async Task<IssueUpdateItem[]> AttachIssueFiles(
        long issueId,
        Guid ownerId,
        MediaInfo[] mediaInfos,
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
        
        await context.SaveChangesAsync(cancellationToken);

        return mediaInfos
            .Select(x => new IssueUpdateItem
            {
                Action = ChangeAction.Create,
                EntityType = IssueUpdateEntityType.Attachment,
                NewValueId = x.OriginalFileId.ToString(),
                NewDisplayValue = x.FileName,
            })
            .ToArray();
    }

    private async Task<IssueUpdateItem[]> DetachIssueAttachments(long issueId, IEnumerable<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        var attachments = await context.IssueAttachments
            .Where(x => x.IssueId == issueId)
            .Where(x => attachmentIds.Contains(x.AttachmentId))
            .Select(x => x.Attachment!)
            .Select(x => new { x.Id, x.File!.Name, FileId = x.File.Id })
            .ToListAsyncEF(cancellationToken);

        await context.Attachments
            .Where(x => attachments.Select(a => a.Id).Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
        
        return attachments
            .Select(x => new IssueUpdateItem
            {
                OldDisplayValue = x.Name,
                Action = ChangeAction.Delete,
                OldValueId = x.FileId.ToString(),
                EntityType = IssueUpdateEntityType.Attachment,
            })
            .ToArray();
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

public abstract record SetIssueAttributeRequest
{
    /// <summary>
    /// The attribute identifier <see cref="DataAccess.Models.Attribute.Id"/>.
    /// </summary>
    public long Id { get; set; }
}

public record SetIssueTextAttributeRequest : SetIssueAttributeRequest
{
    public required string Value { get; set; }
}

public record SetIssueListAttributeRequest : SetIssueAttributeRequest
{
    public required long ListValueId { get; set; }
}

public enum OrderTargetType
{
    After,
    Before,
}

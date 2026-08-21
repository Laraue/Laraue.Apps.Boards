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
    /// <summary>
    /// Creates an issue owned by <paramref name="ownerId"/> from an <see cref="IssueCreateRequest"/>:
    /// only status and creation time are mandatory on it, everything else (content, assignee,
    /// attributes, attachments) is optional and applied via the shared
    /// <see cref="IssueChange{TSelf}"/> setters - same shape as <see cref="Update"/>.
    /// </summary>
    Task<long> Create(
        Guid ownerId,
        IssueCreateRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Applies a partial <see cref="IssueUpdateRequest"/> to an issue: only the properties that were
    /// actually set on it are touched and logged to history. One method regardless of whether
    /// the caller is doing a full Web API edit (content + assignee + attributes + attachments
    /// all at once) or a narrow Telegram sync (content only, or linking one already-stored
    /// attachment).
    /// </summary>
    Task Update(
        long issueId,
        Guid updaterId,
        IssueUpdateRequest request,
        CancellationToken cancellationToken);

    Task Delete(
        long id,
        Guid deleterId,
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
        MediaInfo[] newFiles,
        Guid[] deleteAttachmentIds,
        CancellationToken cancellationToken);
    
    Task DeleteComment(
        long id,
        Guid deleterId,
        CancellationToken cancellationToken);

    Task UpdateIssuesOrder(
        long[] issueIds,
        long targetIssueId,
        OrderTargetType targetType,
        CancellationToken ct);
    
    /// <summary>
    /// Move issue to new status.
    /// </summary>
    Task<Dictionary<string, string>> UpdateIssuesStatus(
        long[] issueIds,
        long newStatusId,
        Guid updaterId,
        CancellationToken ct);
}

public class CoreIssuesService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    ISpaceCounterService spaceCounterService,
    IOrganizationConcurrencyControlService organizationConcurrencyControlService,
    IIssueNumbersService issueNumbersService,
    IIssueHistoryService historyService,
    IOrganizationLogItemFactory logItemFactory)
    : ICoreIssuesService
{
    public async Task<long> Create(
        Guid ownerId,
        IssueCreateRequest request,
        CancellationToken cancellationToken)
    {
        var assigneeId = request.AssigneeId.GetValueOrDefault(ownerId);

        var issueData = await context.Statuses
            .Where(x => x.Id == request.StatusId)
            .Select(x => new
            {
                x.Epic!.SpaceId,
                x.Epic.Space!.OrganizationId,
                x.Epic.Space.Key,
                x.EpicId,
                StatusName = x.Name,
                x.Color,
                SpaceName = x.Epic.Space.Name,
                EpicName = x.Epic.Name,
            })
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

        var content = request.Content.GetValueOrDefault();

        var issue = new Issue
        {
            Content = content,
            OwnerId = ownerId,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.CreatedAt,
            TelegramMessageId = request.TelegramMessageId,
            StatusId = request.StatusId,
            AssigneeId = assigneeId,
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

        var items = new List<OrganizationLogItem>
        {
            logItemFactory.SpaceChanged(
                oldValue: null,
                new IdName<long>(issueData.SpaceId, issueData.SpaceName)),
            logItemFactory.EpicChanged(
                oldValue: null,
                new IdName<long>(issueData.EpicId, issueData.EpicName)),
            logItemFactory.StatusChanged(
                oldValue: null,
                new IdName<long>(request.StatusId, issueData.StatusName)),
        };

        if (!string.IsNullOrEmpty(content))
            items.Add(logItemFactory.ContentChanged(oldValue: null, newValue: content));

        var userData = await context.Users
            .Where(x => x.Id == assigneeId)
            .Select(x => new
            {
                Initials = new UserInitials(x.TelegramFirstName, x.TelegramLastName, x.TelegramUserName),
                x.Color,
            })
            .FirstAsyncEF(cancellationToken);

        items.Add(
            logItemFactory.AssigneeChanged(
                oldValue: null,
                new IdName<Guid>(assigneeId, userData.Initials.DisplayName)));

        if (request.Attributes.IsSet)
        {
            items.AddRange(await UpdateAttributes(
                issue.Id,
                issueData.OrganizationId,
                request.Attributes.Value.ToArray(),
                cancellationToken));
        }

        if (request.NewAttachments.Count != 0)
        {
            items.AddRange(await AttachIssueFiles(
                issue.Id,
                ownerId,
                request.NewAttachments.ToArray(),
                cancellationToken));
        }

        if (request.AttachmentIdsToLink.Count != 0)
        {
            items.AddRange(await LinkExistingAttachments(
                issue.Id,
                request.AttachmentIdsToLink,
                cancellationToken));
        }

        await historyService.Record(
            issue.Id,
            LogEntityType.Issue,
            LogAction.Create,
            issueData.OrganizationId,
            ownerId,
            request.CreatedAt,
            items,
            cancellationToken);

        await TouchEpics([issueData.EpicId], request.CreatedAt, cancellationToken);

        return issue.Id;
    }

    public async Task Update(
        long issueId,
        Guid updaterId,
        IssueUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;

        var issueData = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new
            {
                x.Status!.EpicId,
                x.Status.Epic!.Space!.OrganizationId,
                x.Content,
                x.AssigneeId,
            })
            .FirstAsyncEF(cancellationToken);

        var items = new List<OrganizationLogItem>();

        Action<UpdateSettersBuilder<Issue>> settersBuilder = builder
            => builder.SetProperty(x => x.UpdatedAt, date);

        if (request.Content.IsSet && issueData.Content != request.Content.Value)
        {
            var newContent = request.Content.Value;
            settersBuilder += builder => builder.SetProperty(x => x.Content, newContent);
            items.Add(logItemFactory.ContentChanged(issueData.Content, newContent));
        }

        if (request.AssigneeId.IsSet && issueData.AssigneeId != request.AssigneeId.Value)
        {
            var assigneeId = request.AssigneeId.Value;
            var oldAssigneeId = issueData.AssigneeId;

            var usersData = await context.Users
                .Where(x => x.Id == assigneeId || x.Id == oldAssigneeId)
                .ToDictionaryAsyncEF(
                    x => x.Id,
                    x => new
                    {
                        Initials = new UserInitials(x.TelegramFirstName, x.TelegramLastName, x.TelegramUserName),
                        x.Color,
                    },
                    cancellationToken);

            settersBuilder += builder => builder.SetProperty(x => x.AssigneeId, assigneeId);

            items.Add(logItemFactory.AssigneeChanged(
                new IdName<Guid>(oldAssigneeId, usersData[oldAssigneeId].Initials.DisplayName),
                new IdName<Guid>(assigneeId, usersData[assigneeId].Initials.DisplayName)));
        }

        await context.Issues
            .Where(x => x.Id == issueId)
            .ExecuteUpdateAsync(settersBuilder, cancellationToken);

        if (request.NewAttachments.Count != 0)
        {
            items.AddRange(await AttachIssueFiles(
                issueId,
                updaterId,
                request.NewAttachments.ToArray(),
                cancellationToken));
        }

        if (request.AttachmentIdsToLink.Count != 0)
        {
            items.AddRange(await LinkExistingAttachments(
                issueId,
                request.AttachmentIdsToLink,
                cancellationToken));
        }

        if (request.AttachmentIdsToUnlink.Count != 0)
        {
            items.AddRange(await DetachIssueAttachments(
                issueId,
                request.AttachmentIdsToUnlink,
                cancellationToken));
        }

        if (request.Attributes.IsSet)
        {
            items.AddRange(await UpdateAttributes(
                issueId,
                issueData.OrganizationId,
                request.Attributes.Value.ToArray(),
                cancellationToken));
        }

        var recorded = await historyService.RecordIfChanged(
            issueId,
            LogEntityType.Issue,
            LogAction.Update,
            issueData.OrganizationId,
            updaterId,
            date,
            items,
            cancellationToken);

        if (recorded)
            await TouchEpics([issueData.EpicId], date, cancellationToken);
    }

    /// <summary>
    /// Links any number of already-persisted attachments to an issue in one round trip - ids
    /// already linked to any issue are silently skipped. Batched so a Telegram media group with
    /// several attachments doesn't cost a query+insert per attachment.
    /// </summary>
    private async Task<OrganizationLogItem[]> LinkExistingAttachments(
        long issueId,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken)
    {
        var alreadyLinkedIds = await context.IssueAttachments
            .Where(x => attachmentIds.Contains(x.AttachmentId))
            .Select(x => x.AttachmentId)
            .ToArrayAsyncEF(cancellationToken);

        var idsToLink = attachmentIds.Except(alreadyLinkedIds).ToArray();

        if (idsToLink.Length == 0)
            return [];

        var attachmentsData = await context.Attachments
            .Where(x => idsToLink.Contains(x.Id))
            .Select(x => new { x.Id, x.PreviewFileId, x.File!.Name })
            .ToArrayAsyncEF(cancellationToken);

        context.AddRange(
            attachmentsData.Select(x => new IssueAttachment { IssueId = issueId, AttachmentId = x.Id }));

        await context.SaveChangesAsync(cancellationToken);

        return attachmentsData
            .Select(x => logItemFactory.AttachmentAdded(x.PreviewFileId, x.Name))
            .ToArray();
    }

    private async Task<OrganizationLogItem[]> UpdateAttributes(
        long issueId,
        long organizationId,
        SetIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var changes = new List<OrganizationLogItem>();

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
    
    private async Task<OrganizationLogItem[]> UpdateListAttributes(
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
                x.Attribute!.Color,
            })
            .ToArrayAsyncEF(cancellationToken);
            
        var oldAttributeById =  oldAttributes
            .ToDictionary(x => x.AttributeId);

        var changes = new List<OrganizationLogItem>();

        if (attributeRequests.Length > 0)
        {
            var valueNames = await context.AttributeListValues
                .Where(x => attributeRequests.Select(y => y.Id).Contains(x.AttributeId))
                .Select(x => new { x.Id, x.AttributeId, x.Value, x.Attribute!.Color })
                .ToArrayAsyncEF(cancellationToken);
        
            var valueByAttributeId = valueNames
                .GroupBy(x => x.AttributeId)
                .ToDictionary(
                    x => x.Key,
                    x => x.ToDictionary(y => y.Id));
            
            foreach (var request in attributeRequests)
            {
                var listValueData = valueByAttributeId[request.Id][request.ListValueId];
                
                // Update old
                if (oldAttributeById.TryGetValue(request.Id, out var oldAttribute))
                {
                    if (oldAttribute.AttributeListValueId == request.ListValueId)
                        continue;
                    
                    var entity = new IssueAttributeListValue
                    {
                        Id = oldAttribute.Id,
                        IssueId = issueId,
                        AttributeId = oldAttribute.AttributeId,
                        AttributeListValueId = request.ListValueId,
                    };

                    context.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;
                    
                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = listValueData.Value,
                        OldDisplayValue = oldAttribute.AttributeListValue,
                        PropertyType = PropertyType.Attribute,
                        OldValueId = oldAttribute.AttributeListValueId.ToString(),
                        NewValueId = request.ListValueId.ToString(),
                        PropertyName = attributeNameById[request.Id],
                        ParentId = request.Id.ToString(),
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
                    
                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = listValueData.Value,
                        PropertyType = PropertyType.Attribute,
                        NewValueId = request.ListValueId.ToString(),
                        PropertyName = attributeNameById[request.Id],
                        ParentId = request.Id.ToString(),
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
                    x.AttributeId,
                    AttributeListValueName = x.AttributeListValue!.Value,
                    x.AttributeListValueId,
                })
                .ToDictionaryAsyncEF(x => x.Id, cancellationToken);
            
            foreach (var deletableValue in deletableValues)
            {
                changes.Add(new OrganizationLogItem
                {
                    OldDisplayValue = deletableValue.Value.AttributeListValueName,
                    PropertyType = PropertyType.Attribute,
                    OldValueId = deletableValue.Value.AttributeListValueId.ToString(),
                    PropertyName = attributeNameById[deletableValue.Key],
                    ParentId = deletableValue.Value.AttributeId.ToString(),
                });
            }

            await context.IssueAttributeListValues
                .Where(x => deletableValues.Select(v => v.Key).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        return changes.ToArray();
    }

    private async Task<OrganizationLogItem[]> UpdateTextAttributes(
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

        var changes = new List<OrganizationLogItem>();
        
        if (attributeRequests.Any())
        {
            foreach (var request in attributeRequests)
            {
                // Update old
                if (oldAttributes.TryGetValue(request.Id, out var oldAttribute))
                {
                    if (oldAttribute.Text == request.Value)
                        continue;
                    
                    var entity = new IssueAttributeTextValue
                    {
                        IssueId = issueId,
                        AttributeId = request.Id,
                        Text = request.Value,
                    };
                    
                    context.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;
                    
                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = request.Value,
                        OldDisplayValue = oldAttribute.Text,
                        PropertyType = PropertyType.Attribute,
                        PropertyName = attributeNameById[oldAttribute.AttributeId],
                        ParentId = request.Id.ToString(),
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
                    
                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = request.Value,
                        PropertyType = PropertyType.Attribute,
                        PropertyName = attributeNameById[request.Id],
                        ParentId = request.Id.ToString(),
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
            changes.Add(new OrganizationLogItem
            {
                OldDisplayValue = deletable.Value.Text,
                PropertyType = PropertyType.Attribute,
                PropertyName = attributeNameById[deletable.Key],
                ParentId = deletable.Value.AttributeId.ToString(),
            });
        }
        
        return changes.ToArray();
    }

    public async Task Delete(
        long id,
        Guid deleterId,
        CancellationToken cancellationToken)
    {
        var issueData = await context.Issues
            .Where(x => x.Id == id)
            .Select(x => new
            {
                Key = new IssueKey(x.Status!.Epic!.Space!.Key, x.IssueNumber!.Number),
                x.Status.Epic.Space.OrganizationId,
            })
            .FirstAsyncEF(cancellationToken);
        
        await historyService.Record(
            id,
            LogEntityType.Issue,
            LogAction.Delete,
            issueData.OrganizationId,
            deleterId,
            dateTimeProvider.UtcNow,
            items: null,
            cancellationToken);

        await context.Issues
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
        context.Database.EnsureTransactionStarted();
        
        var issueData = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new
            {
                x.Status!.Epic!.Space!.OrganizationId,
            })
            .FirstAsyncEF(cancellationToken);
        
        var issueComment = new IssueComment
        {
            Text = comment,
            IssueId = issueId,
            OwnerId = ownerId,
            CreatedAt = dateTimeProvider.UtcNow,
            UpdatedAt = dateTimeProvider.UtcNow,
        };

        context.Add(issueComment);

        var attachments = new List<IssueCommentAttachment>();
        
        foreach (var mediaInfo in mediaInfos)
        {
            var attachment = new IssueCommentAttachment
            {
                Comment = issueComment,
                Attachment = GetAttachmentEntity(ownerId, mediaInfo),
            };
        
            attachments.Add(attachment);
        }
        
        context.AddRange(attachments);
        await context.SaveChangesAsync(cancellationToken);

        var items = new List<OrganizationLogItem>
        {
            logItemFactory.ContentChanged(oldValue: null, newValue: comment),
        };

        foreach (var attachment in attachments)
        {
            items.Add(
                logItemFactory.AttachmentAdded(
                    attachment.Attachment!.PreviewFileId,
                    attachment.Attachment.File!.Name));
        }

        await historyService.Record(
            issueComment.Id,
            LogEntityType.Comment,
            LogAction.Create,
            issueData.OrganizationId,
            ownerId,
            dateTimeProvider.UtcNow,
            items,
            cancellationToken);

        return issueComment.Id;
    }

    public async Task UpdateComment(
        long commentId,
        Guid ownerId,
        string comment,
        MediaInfo[] newFiles,
        Guid[] deleteAttachmentIds,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        var commentData = await context.IssueComments
            .Where(x => x.Id == commentId)
            .Select(x => new
            {
                x.IssueId,
                x.Issue!.Status!.Epic!.Space!.OrganizationId,
                x.Text,
            })
            .FirstAsyncEF(cancellationToken);
        
        var items = new List<OrganizationLogItem>();

        if (commentData.Text != comment)
        {
            await context.IssueComments
                .Where(x => x.Id == commentId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.Text, _ => comment),
                    cancellationToken);

            items.Add(logItemFactory.ContentChanged(commentData.Text, comment));
        }

        var commentAttachments = new List<IssueCommentAttachment>();
        
        foreach (var mediaInfo in newFiles)
        {
            var attachment = new IssueCommentAttachment
            {
                CommentId = commentId,
                Attachment = GetAttachmentEntity(ownerId, mediaInfo),
            };
        
            commentAttachments.Add(attachment);
        }

        if (commentAttachments.Count > 0)
        {
            context.AddRange(commentAttachments);
            await context.SaveChangesAsync(cancellationToken);

            foreach (var commentAttachment in commentAttachments)
            {
                items.Add(
                    logItemFactory.AttachmentAdded(
                        commentAttachment.Attachment!.PreviewFileId,
                        commentAttachment.Attachment.File!.Name));
            }
        }

        if (deleteAttachmentIds.Length != 0)
        {
            var deletableAttachments = await context.IssueCommentsAttachments
                .Where(x => x.CommentId == commentId)
                .Where(x => deleteAttachmentIds.Contains(x.AttachmentId))
                .Select(x => new
                {
                    x.AttachmentId,
                    x.Attachment!.PreviewFileId,
                    x.Attachment.File!.Name,
                })
                .ToListAsyncEF(cancellationToken);

            await context.Attachments
                .Where(x => deletableAttachments.Select(a => a.AttachmentId).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var attachment in deletableAttachments)
            {
                items.Add(
                    logItemFactory.AttachmentRemoved(
                        attachment.PreviewFileId,
                        attachment.Name));
            }
        }

        await historyService.RecordIfChanged(
            commentId,
            LogEntityType.Comment,
            LogAction.Update,
            commentData.OrganizationId,
            ownerId,
            dateTimeProvider.UtcNow,
            items,
            cancellationToken);
    }

    public async Task DeleteComment(long id, Guid deleterId, CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        var commentData = await context.IssueComments
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.IssueId,
                x.Issue!.Status!.Epic!.Space!.OrganizationId,
                x.Text,
            })
            .FirstAsyncEF(cancellationToken);

        await context.IssueCommentsAttachments
            .Where(x => x.CommentId == id)
            .Select(x => x.Attachment)
            .ExecuteDeleteAsync(cancellationToken);
        
        await context.IssueComments
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
        
        await historyService.Record(
            id,
            LogEntityType.Comment,
            LogAction.Delete,
            commentData.OrganizationId,
            deleterId,
            dateTimeProvider.UtcNow,
            items: null,
            cancellationToken);
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
    
    public async Task<Dictionary<string, string>> UpdateIssuesStatus(
        long[] issueIds,
        long newStatusId,
        Guid updaterId,
        CancellationToken ct)
    {
        context.Database.EnsureTransactionStarted();
        
        var issuesToUpdate = await context.Issues
            .Where(i => ((IEnumerable<long>)issueIds).Contains(i.Id))
            .Where(i => i.StatusId != newStatusId)
            .Select(i => new
            {
                i.Id,
                i.Status!.Epic!.SpaceId,
                i.Status!.Epic!.Space!.OrganizationId,
                StatusName = i.Status.Name,
                i.StatusId,
                i.Status.Color,
                i.Status.EpicId,
                EpicName = i.Status.Epic.Name,
                SpaceName = i.Status.Epic.Space.Name,
                SpaceKey = i.Status.Epic.Space.Key,
                i.IssueNumber!.Number,
            })
            .ToListAsyncEF(ct);
        
        var newStatusData = await context.Statuses
            .Where(i => i.Id == newStatusId)
            .Select(i => new
            {
                i.Epic!.SpaceId,
                i.Epic!.Space!.OrganizationId,
                StatusName = i.Name,
                i.Color,
                i.EpicId,
                EpicName = i.Epic.Name,
                SpaceName = i.Epic.Space.Name,
            })
            .FirstOrThrowNotFoundEFAsync($"Status: {newStatusId} is not found", ct);

        // TODO - If organization can be changed, is it possible to pass issues from different orgs? Or we will leave that limit?
        if (issuesToUpdate.Any(x => x.OrganizationId != newStatusData.OrganizationId))
            throw new InvalidOperationException("Change issue status works only inside the organization");
        
        await context.Issues
            .Where(i => ((IEnumerable<long>)issueIds).Contains(i.Id))
            .ExecuteUpdateAsync(
                upd =>
                {
                    upd
                        .SetProperty(x => x.StatusId, newStatusId)
                        .SetProperty(x => x.UpdatedAt, dateTimeProvider.UtcNow);
                },
                ct);

        foreach (var issue in issuesToUpdate)
        {
            var logEntry = new OrganizationLog
            {
                CreatedAt = dateTimeProvider.UtcNow,
                EntityId = issue.Id,
                EntityType = LogEntityType.Issue,
                Action = LogAction.Update,
                OrganizationId = issue.OrganizationId,
                OwnerId = updaterId,
                Items = [],
            };

            if (issue.SpaceId != newStatusData.SpaceId)
            {
                logEntry.Items.Add(
                    logItemFactory.SpaceChanged(
                        new IdName<long>(issue.SpaceId, issue.SpaceName),
                        new IdName<long>(newStatusData.SpaceId, newStatusData.SpaceName)));
            }

            if (issue.EpicId != newStatusData.EpicId)
            {
                logEntry.Items.Add(
                    logItemFactory.EpicChanged(
                        new IdName<long>(issue.EpicId, issue.EpicName),
                        new IdName<long>(newStatusData.EpicId, newStatusData.EpicName)));
            }

            logEntry.Items.Add(
                logItemFactory.StatusChanged(
                    new IdName<long>(issue.StatusId, issue.StatusName),
                    new IdName<long>(newStatusId, newStatusData.StatusName)));
            
            context.OrganizationLogs.Add(logEntry);
        }
        
        await context.SaveChangesAsync(ct);
        
        var issuesWithUpdatedSpace = issuesToUpdate
            .Where(i => i.SpaceId != newStatusData.SpaceId)
            .ToArray();
        
        // Issue with non updated space
        var oldToNewKeyMap = issuesToUpdate
            .Except(issuesWithUpdatedSpace)
            .Select(x => new IssueKey(x.SpaceKey, x.Number).ToString())
            .ToDictionary(x => x, x => x);

        if (issuesWithUpdatedSpace.Length == 0)
            return oldToNewKeyMap;
        
        var affectedIssueNumbers = context.IssueNumbers
            .Where(i => issuesWithUpdatedSpace.Select(x => x.Id).Contains(i.IssueId));
        
        await issueNumbersService.UpdateIssueNumbers(affectedIssueNumbers, newStatusData.SpaceId, ct);

        var updatedIssueKeyByIssueId = await context.Issues
            .Where(x => issuesWithUpdatedSpace.Select(y => y.Id).Contains(x.Id))
            .Select(x => new
            {
                Key = new IssueKey(x.Status!.Epic!.Space!.Key, x.IssueNumber!.Number),
                x.Id
            })
            .ToDictionaryAsyncEF(x => x.Id, x => x.Key, ct);
        
        foreach (var issueWithUpdatedSpace in issuesWithUpdatedSpace)
        {
            var key = updatedIssueKeyByIssueId[issueWithUpdatedSpace.Id];
            
            oldToNewKeyMap.Add(
                new IssueKey(issueWithUpdatedSpace.SpaceKey, issueWithUpdatedSpace.Number).ToString(),
                key.ToString());
        }
        
        return oldToNewKeyMap;
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

    private async Task<OrganizationLogItem[]> AttachIssueFiles(
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
            .Select(x => logItemFactory.AttachmentAdded(x.OriginalFileId, x.FileName))
            .ToArray();
    }

    private async Task<OrganizationLogItem[]> DetachIssueAttachments(long issueId, IEnumerable<Guid> attachmentIds, CancellationToken cancellationToken)
    {
        var attachments = await context.IssueAttachments
            .Where(x => x.IssueId == issueId)
            .Where(x => attachmentIds.Contains(x.AttachmentId))
            .Select(x => x.Attachment!)
            .Select(x => new { x.Id, x.File!.Name, x.PreviewFileId })
            .ToListAsyncEF(cancellationToken);

        await context.Attachments
            .Where(x => attachments.Select(a => a.Id).Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);

        return attachments
            .Select(x => logItemFactory.AttachmentRemoved(x.PreviewFileId, x.Name))
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

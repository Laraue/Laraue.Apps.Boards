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
        string content,
        Guid assigneeId,
        SetIssueAttributeRequest[] attributes,
        MediaInfo[] newFiles,
        Guid[] deleteAttachmentIds,
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
        assigneeId ??= ownerId;
        
        var issueData = await context.Statuses
            .Where(x => x.Id == statusId)
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
        
        var issue = new Issue
        {
            Content = text,
            OwnerId = ownerId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            TelegramMessageId = telegramMessageId,
            StatusId = statusId,
            AssigneeId = assigneeId.Value,
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
        
        var change = new OrganizationLog
        {
            CreatedAt = createdAt,
            EntityId = issue.Id,
            EntityType = LogEntityType.Issue,
            Action = LogAction.Create,
            Items =
            [
                GetSpaceLogItem(old: null, @new: new IdName<long>(issueData.SpaceId, issueData.SpaceName)),
                GetEpicLogItem(old: null, @new: new IdName<long>(issueData.EpicId, issueData.EpicName)),
                GetStatusLogItem(old: null, @new: new IdName<long>(statusId, issueData.StatusName)),
            ],
            OrganizationId = issueData.OrganizationId,
            OwnerId = ownerId,
        };

        if (!string.IsNullOrEmpty(text))
            change.Items.Add(GetContentLogItem(oldValue: null, newValue: text));
        
        var userData = await context.Users
            .Where(x => x.Id == assigneeId)
            .Select(x => new
            {
                Initials = new UserInitials(x.TelegramFirstName, x.TelegramLastName, x.TelegramUserName),
                x.Color,
            })
            .FirstAsyncEF(cancellationToken);
        
        change.Items.Add(
            GetAssigneeLogItem(old: null, new IdName<Guid>(assigneeId.Value, userData.Initials.DisplayName)));
        
        change.Items.AddRange(await UpdateAttributes(issue.Id, issueData.OrganizationId, attributes, cancellationToken));
        change.Items.AddRange(await AttachIssueFiles(issue.Id, ownerId, newFiles, cancellationToken));
        
        context.Add(change);
        await context.SaveChangesAsync(cancellationToken);
        await TouchEpics([issueData.EpicId], createdAt, cancellationToken);
        
        return issue.Id;
    }

    public async Task Update(
        long issueId,
        Guid updaterId,
        string content,
        Guid assigneeId,
        SetIssueAttributeRequest[] attributes,
        MediaInfo[] newFiles,
        Guid[] deleteAttachmentIds,
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

        var change = new OrganizationLog
        {
            CreatedAt = date,
            EntityId = issueId,
            EntityType = LogEntityType.Issue,
            Items = [],
            OrganizationId = issueData.OrganizationId,
            OwnerId = updaterId,
            Action = LogAction.Update,
        };

        Action<UpdateSettersBuilder<Issue>> settersBuilder = builder
            => builder.SetProperty(x => x.UpdatedAt, date);

        var oldContent = issueData.Content;
        if (oldContent != content)
        {
            settersBuilder += builder => builder.SetProperty(x => x.Content, content);
            change.Items.Add(GetContentLogItem(oldContent, content));
        }

        var oldAssigneeId = issueData.AssigneeId;
        if (oldAssigneeId != assigneeId)
        {
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
            
            change.Items.Add(new OrganizationLogItem
            {
                NewDisplayValue = usersData[assigneeId].Initials.DisplayName,
                OldDisplayValue = usersData[oldAssigneeId].Initials.DisplayName,
                NewValueId = assigneeId.ToString(),
                OldValueId = issueData.AssigneeId.ToString(),
                PropertyType = PropertyType.Assignee,
            });
        }

        await context.Issues
            .Where(x => x.Id == issueId)
            .ExecuteUpdateAsync(settersBuilder, cancellationToken);
        
        change.Items.AddRange(await AttachIssueFiles(issueId, updaterId, newFiles, cancellationToken));
        change.Items.AddRange(await DetachIssueAttachments(issueId, deleteAttachmentIds, cancellationToken));
        change.Items.AddRange(await UpdateAttributes(issueId, issueData.OrganizationId, attributes, cancellationToken));

        if (change.Items.Count != 0)
        {
            context.Add(change);
            await context.SaveChangesAsync(cancellationToken);
            await TouchEpics([issueData.EpicId], date, cancellationToken);
        }
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
        
        context.Add(new OrganizationLog
        {
            CreatedAt = dateTimeProvider.UtcNow,
            EntityId = id,
            EntityType = LogEntityType.Issue,
            Action = LogAction.Delete,
            OrganizationId = issueData.OrganizationId,
            OwnerId = deleterId,
        });

        await context.SaveChangesAsync(cancellationToken);
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
        
        var logEntity = new OrganizationLog
        {
            CreatedAt = dateTimeProvider.UtcNow,
            OrganizationId = issueData.OrganizationId,
            EntityId = issueComment.Id,
            EntityType = LogEntityType.Comment,
            Action = LogAction.Create,
            OwnerId = ownerId,
            Items =
            [
                GetContentLogItem(oldValue: null, newValue: comment),
            ]
        };

        foreach (var attachment in attachments)
        {
            logEntity.Items.Add(
                GetAttachmentAddedLogItem(
                    attachment.Attachment!.PreviewFileId,
                    attachment.Attachment.File!.Name));
        }

        context.OrganizationLogs.Add(logEntity);
        await context.SaveChangesAsync(cancellationToken);
        
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
        
        var logEntity = new OrganizationLog
        {
            CreatedAt = dateTimeProvider.UtcNow,
            OrganizationId = commentData.OrganizationId,
            EntityId = commentId,
            EntityType = LogEntityType.Comment,
            Action = LogAction.Update,
            OwnerId = ownerId,
            Items = [],
        };

        if (commentData.Text != comment)
        {
            await context.IssueComments
                .Where(x => x.Id == commentId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.Text, _ => comment),
                    cancellationToken);
            
            logEntity.Items.Add(GetContentLogItem(commentData.Text, comment));
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
                logEntity.Items.Add(
                    GetAttachmentAddedLogItem(
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
                logEntity.Items.Add(
                    GetAttachmentDeletedLogItem(
                        attachment.PreviewFileId,
                        attachment.Name));
            }
        }

        if (logEntity.Items.Count != 0)
        {
            context.Add(logEntity);
            await context.SaveChangesAsync(cancellationToken);
        }
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
        
        var logEntity = new OrganizationLog
        {
            CreatedAt = dateTimeProvider.UtcNow,
            OrganizationId = commentData.OrganizationId,
            EntityId = id,
            EntityType = LogEntityType.Comment,
            Action = LogAction.Delete,
            OwnerId = deleterId,
            Items = [],
        };
        
        context.Add(logEntity);
        await context.SaveChangesAsync(cancellationToken);
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
                    GetSpaceLogItem(
                        new IdName<long>(issue.SpaceId, issue.SpaceName),
                        new IdName<long>(newStatusData.SpaceId, newStatusData.SpaceName)));
            }

            if (issue.EpicId != newStatusData.EpicId)
            {
                logEntry.Items.Add(
                    GetEpicLogItem(
                        new IdName<long>(issue.EpicId, issue.EpicName),
                        new IdName<long>(newStatusData.EpicId, newStatusData.EpicName)));
            }
            
            logEntry.Items.Add(
                GetStatusLogItem(
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
            .Select(x => GetAttachmentAddedLogItem(x.OriginalFileId, x.FileName))
            .ToArray();
    }

    private static OrganizationLogItem GetAttachmentAddedLogItem(Guid? previewFileId, string? fileName)
    {
        return new OrganizationLogItem
        {
            PropertyType = PropertyType.Attachment,
            NewValueId = previewFileId.ToString(),
            NewDisplayValue = fileName,
        };
    }

    private static OrganizationLogItem GetAttachmentDeletedLogItem(Guid? previewFileId, string? fileName)
    {
        return new OrganizationLogItem
        {
            PropertyType = PropertyType.Attachment,
            OldValueId = previewFileId.ToString(),
            OldDisplayValue = fileName,
        };
    }

    private static OrganizationLogItem GetEpicLogItem(IdName<long>? old, IdName<long>? @new)
    {
        return GetLogItem(PropertyType.Epic, old, @new);
    }

    private static OrganizationLogItem GetSpaceLogItem(IdName<long>? old, IdName<long>? @new)
    {
        return GetLogItem(PropertyType.Space, old, @new);
    }

    private static OrganizationLogItem GetStatusLogItem(IdName<long>? old, IdName<long>? @new)
    {
        return GetLogItem(PropertyType.Status, old, @new);
    }

    private static OrganizationLogItem GetAssigneeLogItem(IdName<Guid>? old, IdName<Guid>? @new)
    {
        return GetLogItem(PropertyType.Assignee, old, @new);
    }
    
    private static OrganizationLogItem GetLogItem<T>(
        PropertyType propertyType,
        IdName<T>? oldValue,
        IdName<T>? newValue) where T : struct
    {
        var item = new OrganizationLogItem
        {
            PropertyType = propertyType,
            NewDisplayValue = newValue?.Name,
            OldDisplayValue = oldValue?.Name,
        };
        
        if (oldValue.HasValue)
            item.OldValueId = oldValue.Value.Id.ToString();
        
        if (newValue.HasValue)
            item.NewValueId = newValue.Value.Id.ToString();
        
        return item;
    }

    private record struct IdName<T>(T Id, string Name) where T : struct;
    
    private static OrganizationLogItem GetContentLogItem(string? oldValue, string? newValue)
    {
        return new OrganizationLogItem
        {
            NewDisplayValue = newValue,
            OldDisplayValue = oldValue,
            PropertyType = PropertyType.Content,
        };
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
            .Select(x => GetAttachmentDeletedLogItem(x.PreviewFileId, x.Name))
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

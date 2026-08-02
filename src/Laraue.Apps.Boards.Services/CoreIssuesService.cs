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
            IssueId = issue.Id,
            Items =
            [
                new OrganizationLogItem
                {
                    Action = ChangeAction.Create,
                    EntityType = IssueUpdateEntityType.Issue,
                    NewDisplayValue = new IssueKey(issueData.Key, issueNumber.Number).ToString(),
                },
                new OrganizationLogItem
                {
                    Action = ChangeAction.Update,
                    EntityType = IssueUpdateEntityType.Status,
                    NewDisplayValue = issueData.StatusName,
                    NewValueData = new ValueData
                    {
                        ValueId = statusId.ToString(),
                        Color = issueData.Color,
                    },
                }
            ],
            OrganizationId = issueData.OrganizationId,
            OwnerId = ownerId,
        };

        if (!string.IsNullOrEmpty(text))
        {
            change.Items.Add(new OrganizationLogItem
            {
                NewDisplayValue = text,
                Action = ChangeAction.Update,
                EntityType = IssueUpdateEntityType.Content,
            });
        }
        
        var userData = await context.Users
            .Where(x => x.Id == assigneeId)
            .Select(x => new
            {
                Initials = new UserInitials(x.TelegramFirstName, x.TelegramLastName, x.TelegramUserName),
                x.Color,
            })
            .FirstAsyncEF(cancellationToken);
        
        change.Items.Add(new OrganizationLogItem
        {
            NewDisplayValue = userData.Initials.DisplayName,
            NewValueData = new ValueData
            {
                ValueId = assigneeId.ToString(),
                Color = userData.Color,
            },
            Action = ChangeAction.Update,
            EntityType = IssueUpdateEntityType.Assignee,
        });
        
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
            IssueId = issueId,
            Items = [],
            OrganizationId = issueData.OrganizationId,
            OwnerId = updaterId,
        };

        Action<UpdateSettersBuilder<Issue>> settersBuilder = builder
            => builder.SetProperty(x => x.UpdatedAt, date);

        var oldContent = issueData.Content;
        if (oldContent != content)
        {
            settersBuilder += builder => builder.SetProperty(x => x.Content, content);
            change.Items.Add(new OrganizationLogItem
            {
                NewDisplayValue = content,
                OldDisplayValue = oldContent,
                Action = ChangeAction.Update,
                EntityType = IssueUpdateEntityType.Content,
            });
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
                NewValueData = new ValueData
                {
                    ValueId = assigneeId.ToString(),
                    Color = usersData[assigneeId].Color,
                },
                OldValueData = new ValueData
                {
                    ValueId = issueData.AssigneeId.ToString(),
                    Color = usersData[oldAssigneeId].Color,
                },
                Action = ChangeAction.Update,
                EntityType = IssueUpdateEntityType.Assignee,
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
                        EntityType = IssueUpdateEntityType.Property,
                        Action = ChangeAction.Update,
                        OldValueData = new ValueData
                        {
                            ValueId = oldAttribute.AttributeListValueId.ToString(),
                            Color = oldAttribute.Color,
                        },
                        NewValueData = new ValueData
                        {
                            ValueId = request.ListValueId.ToString(),
                            Color = oldAttribute.Color,
                        },
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
                    
                    changes.Add(new OrganizationLogItem
                    {
                        NewDisplayValue = listValueData.Value,
                        EntityType = IssueUpdateEntityType.Property,
                        Action = ChangeAction.Create,
                        NewValueData =  new ValueData
                        {
                            ValueId = request.ListValueId.ToString(),
                            Color = listValueData.Color,
                        },
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
                changes.Add(new OrganizationLogItem
                {
                    OldDisplayValue = deletableValue.Value.AttributeListValueName,
                    EntityType = IssueUpdateEntityType.Property,
                    Action = ChangeAction.Delete,
                    OldValueData = new ValueData
                    {
                        ValueId = deletableValue.Value.AttributeListValueId.ToString(),
                    },
                    PropertyName = attributeNameById[deletableValue.Key],
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
                    
                    changes.Add(new OrganizationLogItem
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
            changes.Add(new OrganizationLogItem
            {
                OldDisplayValue = deletable.Value.Text,
                EntityType = IssueUpdateEntityType.Property,
                Action = ChangeAction.Delete,
                PropertyName = attributeNameById[deletable.Key],
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
            Items =
            [
                new OrganizationLogItem
                {
                    Action = ChangeAction.Delete,
                    EntityType = IssueUpdateEntityType.Issue,
                    OldDisplayValue = issueData.Key.ToString(),
                }
            ],
            IssueId = id,
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
            IssueId = issueId,
            OwnerId = ownerId,
            Items =
            [
                new OrganizationLogItem
                {
                    Action = ChangeAction.Create,
                    EntityType = IssueUpdateEntityType.CommentContent,
                    NewDisplayValue = comment,
                }
            ]
        };

        foreach (var attachment in attachments)
        {
            logEntity.Items.Add(new OrganizationLogItem
            {
                Action = ChangeAction.Create,
                EntityType = IssueUpdateEntityType.CommentAttachment,
                NewValueData = new ValueData
                {
                    ParentValueId = issueComment.Id.ToString(),
                    ValueId = attachment.Attachment!.FileId.ToString(),
                },
                NewDisplayValue = attachment.Attachment.File!.Name,
            });
        }

        context.OrganizationLogs.Add(logEntity);
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
        Guid updaterId,
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
                StatusName = i.Status.Name,
                i.StatusId,
                i.Status.Color,
            })
            .ToListAsyncEF(ct);
        
        var newSpaceData = await context.Statuses
            .Where(i => i.Id == statusId)
            .Select(i => new { i.Epic!.SpaceId, i.Epic!.Space!.OrganizationId, StatusName = i.Name, i.Color })
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

        foreach (var issue in oldIssuesData)
        {
            context.OrganizationLogs.Add(new OrganizationLog
            {
                CreatedAt = dateTimeProvider.UtcNow,
                IssueId = issue.Id,
                OrganizationId = issue.OrganizationId,
                OwnerId = updaterId,
                Items =
                [
                    new OrganizationLogItem
                    {
                        Action = ChangeAction.Update,
                        EntityType = IssueUpdateEntityType.Status,
                        OldValueData = new ValueData
                        {
                            ValueId = issue.StatusId.ToString(),
                            Color = issue.Color,
                        },
                        OldDisplayValue = issue.StatusName,
                        NewValueData = new ValueData
                        {
                            ValueId = statusId.ToString(),
                            Color = newSpaceData.Color,
                        },
                        NewDisplayValue = newSpaceData.StatusName,
                    }
                ]
            });
        }
        
        await context.SaveChangesAsync(ct);

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
            .Select(x => new OrganizationLogItem
            {
                Action = ChangeAction.Create,
                EntityType = IssueUpdateEntityType.Attachment,
                NewValueData = new ValueData
                {
                    ValueId = x.OriginalFileId.ToString(),
                },
                NewDisplayValue = x.FileName,
            })
            .ToArray();
    }

    private async Task<OrganizationLogItem[]> DetachIssueAttachments(long issueId, IEnumerable<Guid> attachmentIds, CancellationToken cancellationToken)
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
            .Select(x => new OrganizationLogItem
            {
                OldDisplayValue = x.Name,
                Action = ChangeAction.Delete,
                OldValueData = new ValueData
                {
                    ValueId = x.FileId.ToString(),
                },
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

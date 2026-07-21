using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Laraue.Apps.Boards.Services;

public interface ICoreIssuesService
{
    Task<long> Create(
        CreateIssueRequest request,
        CancellationToken cancellationToken);
    
    Task Update(
        long issueId,
        Action<UpdateSettersBuilder<Issue>> setters,
        CancellationToken cancellationToken);
    
    Task UpdateAttributes(
        long issueId,
        UpdateIssueAttributeRequest[] attributeRequests,
        CancellationToken cancellationToken);
    
    Task Delete(
        long id,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Upload the file and link it to the issue.
    /// </summary>
    Task<MediaInfo> UploadAttachment(
        Guid ownerId,
        long issueId,
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken);
}

public class CoreIssuesService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    ISpaceCounterService spaceCounterService,
    ICoreFilesService coreFilesService)
    : ICoreIssuesService
{
    public async Task<long> Create(
        CreateIssueRequest request,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        var spaceId = await context.Statuses
            .Where(x => x.Id == request.StatusId)
            .Select(x => x.Epic!.SpaceId)
            .FirstOrThrowNotFoundEFAsync("Space was not found", cancellationToken);
        
        var issue = new Issue
        {
            Content = request.Text,
            OwnerId = request.UserId,
            CreatedAt = request.CreatedAt,
            UpdatedAt = request.CreatedAt,
            TelegramMessageId = request.TelegramMessageId,
            StatusId = request.StatusId,
            AssigneeId = request.AssigneeId ?? request.UserId,
        };
        
        var issueNumber = new IssueNumber
        {
            Number = await spaceCounterService.GetNextNumber(spaceId, cancellationToken),
            Issue = issue,
            SpaceId = spaceId,
        };
        
        context.Add(issue);
        context.Add(issueNumber);
        
        await context.SaveChangesAsync(cancellationToken);
        await TouchMessageBoard(issue.Id, request.CreatedAt, cancellationToken);
        
        return issue.Id;
    }

    public async Task Update(
        long issueId,
        Action<UpdateSettersBuilder<Issue>> setters,
        CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;

        await context.Issues
            .Where(x => x.Id == issueId)
            .ExecuteUpdateAsync(
                upd =>
                {
                    setters(upd);
                    upd.SetProperty(x => x.UpdatedAt, date);
                },
                cancellationToken);
        
        await TouchMessageBoard(issueId, date, cancellationToken);
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

    public async Task<MediaInfo> UploadAttachment(
        Guid ownerId,
        long issueId,
        string fileName,
        string contentType,
        Stream stream,
        CancellationToken cancellationToken)
    {
        var fileData = await coreFilesService.UploadFile(fileName, contentType, stream, cancellationToken);
        var attachment = new IssueAttachment
        {
            IssueId = issueId,
            Attachment = new Attachment
            {
                CreatedAt = dateTimeProvider.UtcNow,
                OwnerId = ownerId,
                PreviewFileId = fileData.PreviewFileId,
                FileId = fileData.OriginalFileId,
                Type = fileData.Type,
            }
        };
        
        context.Add(attachment);
        await context.SaveChangesAsync(cancellationToken);

        return fileData;
    }

    private Task<int> TouchMessageBoard(long issueId, DateTime touchedAt, CancellationToken ct)
    {
        return context.Issues.Where(x => x.Id == issueId)
            .Select(x => x.Status!.Epic)
            .ExecuteUpdateAsync(x => x
                .SetProperty(
                    p => p!.TouchedAt,
                    old => old!.TouchedAt > touchedAt ? old.TouchedAt : touchedAt),
                ct);
    }
}

public class CreateIssueRequest
{
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Set the issue assignee. When the field is not set the creator id will be used.
    /// </summary>
    public Guid? AssigneeId { get; set; }
    
    public required string? Text { get; set; }
    public required DateTime CreatedAt { get; set; }
    public long? TelegramMessageId { get; set; }
    public long StatusId { get; set; }
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
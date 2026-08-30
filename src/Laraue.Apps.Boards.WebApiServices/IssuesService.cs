using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Extensions;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Apps.Boards.Services.Sorting;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Extensions;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Attribute = Laraue.Apps.Boards.DataAccess.Models.Attribute;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IIssuesService
{
    Task<BatchResult<IssueListDto>> GetIssues(
        GetIssuesRequest request,
        CancellationToken cancellationToken);
    
    Task<ColumnIssues[]> GetBoard(
        GetBoardRequest request,
        CancellationToken cancellationToken);
    
    Task<EpicSummary[]> GetBoardSummary(
        GetBoardSummaryRequest request,
        CancellationToken cancellationToken);
    
    Task Delete(
        DeleteIssueRequest request,
        CancellationToken ct);
    
    Task<string> Create(
        CreateIssueRequest request,
        CancellationToken ct);
    
    Task Update(
        UpdateIssueRequest request,
        CancellationToken ct);
    
    Task<ShortPaginatedResult<SearchIssueDto>> Search(
        SearchRequest request,
        CancellationToken ct);
    
    Task<IssueDetailDto> GetIssue(
        GetIssueRequest request,
        CancellationToken cancellationToken);
    
    Task<long> AddIssueComment(
        AddCommentRequest request,
        CancellationToken cancellationToken);
    
    Task UpdateIssueComment(
        UpdateCommentRequest request,
        CancellationToken cancellationToken);
    
    Task DeleteIssueComment(
        DeleteCommentRequest request,
        CancellationToken cancellationToken);
    
    Task ChangesIssuesOrder(
        ChangesIssuesOrderRequest request,
        CancellationToken ct);

    Task<Dictionary<string, string>> UpdateIssuesStatus(
        UpdateIssuesStatusRequest request,
        CancellationToken ct);

    Task<ShortPaginatedResult<CommentDto>> GetIssueComments(
        GetIssueCommentsRequest request,
        CancellationToken ct);

    Task<string> SummarizeContent(
        SummarizeIssueContentRequest request,
        CancellationToken cancellationToken);
}

public class IssuesService(
    DatabaseContext context,
    ICoreIssuesService issuesService,
    IAccessService accessService,
    IDateTimeProvider dateTimeProvider,
    ICoreFilesService coreFilesService,
    ICoreSpacesService coreSpacesService,
    IAiContentSummarizer aiContentSummarizer,
    ILogger<IssuesService> logger)
    : IIssuesService
{
    public async Task<BatchResult<IssueListDto>> GetIssues(
        GetIssuesRequest request,
        CancellationToken cancellationToken)
    {
        var statusData = await context.Statuses
            .Where(x => x.Id == request.StatusId)
            .Select(x => new { x.EpicId })
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.EntityNotFound, "Status", request.StatusId), cancellationToken);
        
        await accessService.GetAvailableEpics(
            request.AuthData,
            q => q
                .Where(x => x.Id == statusData.EpicId)
                .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.EntityNotFound, "Status", request.StatusId), cancellationToken),
            cancellationToken);

        var query = context.Issues
            .Where(i => i.StatusId == request.StatusId);

        query = await ApplyFilters(query, request, cancellationToken);
        query = await ApplySorting(query, request, cancellationToken);
            
        if (!string.IsNullOrEmpty(request.SearchString))
        {
            query = query
                .Where(x => x.Content!
                    .ILike(request.SearchString.AsSearchable()));
        }

        var temporaryResult = ProjectToTemporaryDto(query);
        var result = await ToBatchResult(temporaryResult, request, cancellationToken);

        var projected = result.Data
            .Select(Map)
            .ToArray();
        
        await EnrichAttributes(projected, cancellationToken);

        return new BatchResult<IssueListDto>
        {
            HasNext = result.HasNext,
            Data = projected,
            Offset = result.Offset,
        };
    }

    private static async Task<BatchResult<T>> ToBatchResult<T>(
        IQueryable<T> queryable,
        BatchRequest request,
        CancellationToken cancellationToken)
    {
        var requested = await queryable
            .Skip(request.Skip)
            .Take(request.Take + 1)
            .ToListAsyncLinqToDB(cancellationToken);
        
        var hasNext = request.Take < requested.Count;
        var result = requested.Take(request.Take).ToArray();
        
        return new BatchResult<T>
        {
            HasNext = hasNext,
            Data = result,
            Offset = request.Skip + result.Length
        };
    }

    public async Task<ColumnIssues[]> GetBoard(
        GetBoardRequest request,
        CancellationToken cancellationToken)
    {
        await accessService.GetAvailableEpics(
            request.AuthData,
            q => q
                .Where(x => x.Id == request.EpicId)
                .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.EntityNotFound, "Epic", request.EpicId), cancellationToken),
            cancellationToken);
        
        var statusIds = await context.Statuses
            .Where(x => x.EpicId == request.EpicId)
            .Select(x => x.Id)
            .ToListAsyncEF(cancellationToken);

        var result = new List<ColumnIssues>();
        
        var commonQuery = context.Issues.AsQueryable();
        commonQuery = await ApplyFilters(commonQuery, request, cancellationToken);
        commonQuery = await ApplySorting(commonQuery, request, cancellationToken);
        
        foreach (var statusId in statusIds)
        {
            var query = commonQuery
                .Where(x => x.StatusId == statusId);
            
            if (!string.IsNullOrEmpty(request.SearchString))
                query = query
                    .Where(x => x.Content!.ILike(request.SearchString.AsSearchable()));
            
            var statusResult = await ProjectToTemporaryDto(query)
                .FullPaginateLinq2DbAsync(
                    new PaginationData
                    {
                        Page = 0,
                        PerPage = request.Take,
                    },
                    cancellationToken);

            var mappedStatusResult = new InitialBatchResult<IssueListDto>
            {
                Data = statusResult.Data.Select(Map).ToArray(),
                HasNext = statusResult.HasNextPage,
                Offset = statusResult.Data.Count,
                TotalCount = statusResult.Total,
            };
            
            result.Add(new ColumnIssues
            {
                StatusId = statusId,
                Items = mappedStatusResult,
            });
        }

        var allData = result
            .SelectMany(x => x.Items.Data)
            .ToList();
        
        await EnrichAttributes(allData, cancellationToken);

        return result.ToArray();
    }

    public async Task<EpicSummary[]> GetBoardSummary(
        GetBoardSummaryRequest request,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(
            request.AuthData.OrganizationId,
            request.SpaceKey,
            cancellationToken);

        var epics = await accessService.GetAvailableEpics(
            request.AuthData,
            epics => epics
                .Where(x => x.SpaceId == spaceId)
                .Select(x => new
                {
                    x.Id,
                    x.Color,
                    x.Name,
                    x.IsDefault,
                    x.TouchedAt,
                })
                .ToArrayAsyncEF(cancellationToken),
            cancellationToken);

        var epicById = epics.ToDictionary(x => x.Id);
        
        var statusByCategoryId = (await context.Statuses
            .Where(x => epicById.Keys.Contains(x.EpicId))
            .Select(x => new
            {
                x.Id,
                x.Color,
                x.Name,
                x.SortOrder,
                MessageCategoryId = x.EpicId,
            })
            .ToArrayAsyncEF(cancellationToken))
         .ToLookup(x => x.MessageCategoryId);
        
        var counts = (await context.Issues
            .Where(x =>  epics.Select(e => e.Id).Contains(x.Status!.EpicId))
            .Select(x => x)
            .GroupBy(x => x.StatusId)
            .Select(x => new
            {
                Id = x.Key,
                Count = x.Count(),
            })
            .ToArrayAsyncEF(cancellationToken))
            .ToDictionary(x => x.Id, x => x.Count);

        var result = epicById
            .Select(category => new EpicSummary
            {
                Id = category.Key,
                Color = category.Value.Color,
                Name = category.Value.Name,
                TouchedAt = category.Value.TouchedAt,
                IsDefault = category.Value.IsDefault,
                Columns = statusByCategoryId[category.Key]
                    .OrderBy(s => s.SortOrder)
                    .Select(s => new ColumnSummary
                    {
                        Id = s.Id,
                        Color = s.Color,
                        Name = s.Name,
                        Count = counts.GetValueOrDefault(s.Id, 0),
                    })
                    .ToArray()
            })
            .ToArray();

        return result;
    }

    public async Task Delete(DeleteIssueRequest request, CancellationToken ct)
    {
        var issueId = await GetIssueIdByIssueKey(request.AuthData.OrganizationId, request.IssueKey, ct);
        
        await accessService.GetAccessLevelsByIssueId(request.AuthData, issueId, ct)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Issue", request.IssueKey))
            .EnsureOrThrowForbidden(a => a.CanDeleteIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", request.IssueKey, "delete"));

        await issuesService.Delete(issueId, request.AuthData.UserId, ct);
    }

    public async Task<string> Create(CreateIssueRequest request, CancellationToken ct)
    {
        var validationData = await context.Statuses
            .Where(s => s.Id == request.StatusId)
            .Select(x => new { x.EpicId })
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.EntityNotFound, "Status", request.StatusId), ct);
        
        await accessService.GetAccessLevelsByEpicId(request.AuthData, validationData.EpicId, ct)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Status", request.StatusId))
            .EnsureOrThrowNotFound(a => a.CanCreateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Status", request.StatusId, "issue creation"));

        if (FilesHasError(request.Files, out var error))
            throw new BadRequestException(nameof(request.Files), error);
        
        await EnsureUserBelongsToOrganization(request.AuthData, request.AssigneeId, ct);
        
        var attributeUpdateRequests = await GetAttributeUpdateRequests(
            request.AuthData.OrganizationId,
            request.AttributeValues,
            ct);

        var uploadedFiles = await UploadFiles(request.Files, ct);
        
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        
        var issueCreate = new IssueCreateRequest(request.StatusId, dateTimeProvider.UtcNow)
            .SetContent(request.Content)
            .SetAssignee(request.AssigneeId)
            .SetAttributes(attributeUpdateRequests)
            .LinkNewAttachments(uploadedFiles);

        var id = await issuesService.Create(request.AuthData.UserId, issueCreate, ct);

        await transaction.CommitAsync(ct);

        var issueKey = await context.Issues
            .Where(x => x.Id == id)
            .Select(x => new IssueKey(x.IssueNumber!.Space!.Key, x.IssueNumber.Number))
            .FirstAsyncEF(ct);
        
        return issueKey.ToString();
    }

    public async Task Update(UpdateIssueRequest request, CancellationToken ct)
    {
        var issueId = await GetIssueIdByIssueKey(
            request.AuthData.OrganizationId,
            request.IssueKey.GetValueOrDefault(),
            ct);
        
        await accessService.GetAccessLevelsByIssueId(request.AuthData, issueId, ct)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Issue", request.IssueKey))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", request.IssueKey, "update"));

        if (FilesHasError(request.AddFiles, out var error))
            throw new BadRequestException(nameof(request.AddFiles), error);
        
        await EnsureUserBelongsToOrganization(request.AuthData, request.AssigneeId, ct);
        
        var attributeUpdateRequests = await GetAttributeUpdateRequests(
            request.AuthData.OrganizationId,
            request.AttributeValues,
            ct);
        
        var uploadedFiles = await UploadFiles(request.AddFiles, ct);
        
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        
        var issueUpdate = new IssueUpdateRequest()
            .SetContent(request.Content)
            .SetAssignee(request.AssigneeId)
            .SetAttributes(attributeUpdateRequests)
            .LinkNewAttachments(uploadedFiles)
            .UnlinkAttachments(request.RemoveAttachmentIds);

        await issuesService.Update(
            issueId,
            request.AuthData.UserId,
            issueUpdate,
            ct);

        await transaction.CommitAsync(ct);
    }

    public async Task<string> SummarizeContent(SummarizeIssueContentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await aiContentSummarizer.SummarizeAsync(request.Content, cancellationToken);
        }
        catch (AiContentSummarizationException ex)
        {
            logger.LogWarning(ex, "AI summarization failed for organization {OrganizationId}", request.AuthData.OrganizationId);
            throw new AiSummarizationUnavailableException(ErrorMessages.AiSummarizationUnavailable);
        }
    }

    private static bool FilesHasError(IEnumerable<IFormFile> files, [NotNullWhen(true)] out string? error)
    {
        foreach (var file in files)
        {
            if (file.Length > 3_000_000)
            {
                error = ErrorMessages.FileSizeLimited;
                return true;
            }

            if (!SystemMimeTypes.Supported.Contains(file.ContentType))
            {
                error = string.Format(ErrorMessages.UnsupportedMimeTypes, string.Join(", ", SystemMimeTypes.Supported));
                return true;
            }
        }

        error = null;
        return false;
    }

    private async Task EnsureUserBelongsToOrganization(
        OrganizationAuthData authData,
        Guid userId,
        CancellationToken ct)
    {
        var userExists = await accessService.GetOrganizationMembers(
            authData.OrganizationId,
            members =>
            {
                return members
                    .Where(x => x.UserId == userId)
                    .AnyAsyncEF(ct);
            });
        
        if (!userExists)
            throw new NotFoundException(string.Format(ErrorMessages.UserNotBelongsToOrganization, userId));
    }
    
    public async Task<ShortPaginatedResult<SearchIssueDto>> Search(
        SearchRequest request,
        CancellationToken ct)
    {
        var temporaryResult = await accessService.GetAvailableIssues(
            request.AuthData,
            async issues =>
            {
                if (request.EpicIds.Length > 0)
                    issues = issues.Where(x => ((IEnumerable<long>)request.EpicIds).Contains(x.Status!.EpicId));

                if (request.EpicStatuses.Length > 0)
                    issues = issues.Where(x => ((IEnumerable<EpicStatus>)request.EpicStatuses).Contains(x.Status!.Epic!.Status));

                if (request.SpaceKeys.Length > 0)
                {
                    var spaceIds = await context.Spaces
                        .Where(x => x.OrganizationId == request.AuthData.OrganizationId)
                        .Where(x => ((IEnumerable<string>)request.SpaceKeys).Contains(x.Key))
                        .Select(x => x.Id)
                        .ToArrayAsyncEF(ct);
                    
                    if (spaceIds.Length > 0)
                        issues = issues.Where(x => ((IEnumerable<long>)spaceIds).Contains(x.Status!.Epic!.SpaceId));
                }
                
                issues = await ApplyFilters(issues, request, ct);
                issues = await ApplySorting(issues, request, ct);
        
                if (!string.IsNullOrEmpty(request.SearchString))
                    issues = issues
                        .Where(x => x.Content!.ILike(request.SearchString.AsSearchable()));

                return await ProjectToTemporaryDto(issues)
                    .ShortPaginateLinq2DbAsync(request, ct);
            }, ct);
        
        var mapped = temporaryResult.MapTo(Map);
        await EnrichAttributes(mapped.Data, ct);
        
        var result = await MapToSearchDtos(request.AuthData, mapped.Data, ct);
        return new ShortPaginatedResult<SearchIssueDto>(
            mapped.Page,
            mapped.PerPage,
            mapped.HasNextPage,
            result);
    }

    public async Task<IssueDetailDto> GetIssue(
        GetIssueRequest request,
        CancellationToken cancellationToken)
    {
        var issueId = await GetIssueIdByIssueKey(request.AuthData.OrganizationId, request.IssueKey, cancellationToken);
        
        var issueAccessLevels = await accessService.GetAccessLevelsByIssueId(request.AuthData, issueId, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", request.IssueKey));

        var result = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new IssueDetailDtoData
            {
                Id = x.Id,
                AssigneeId = x.AssigneeId,
                AssigneeDisplayName = x.Assignee!.DisplayName,
                AssigneeInitials = x.Assignee.Initials,
                AssigneeColor = x.Assignee.Color,
                Content = x.Content,
                Time = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                CategoryId = x.Status!.EpicId,
                CategoryName = x.Status!.Epic!.Name,
                StatusId = x.StatusId,
                StatusName = x.Status!.Epic!.IsDefault ? null : x.Status!.Name,
                OwnerDisplayName = x.Owner!.DisplayName,
                OwnerInitials = x.Owner.Initials,
                TelegramId = x.Owner.TelegramId,
                OwnerColor = x.Owner.Color,
                CategoryColor = x.Status.Epic.Color,
                StatusColor = x.Status!.Epic!.IsDefault ? null : x.Status.Color,
                OrganizationId = x.Status.Epic.Space!.OrganizationId,
                Number = x.IssueNumber!.Number,
                SpaceId = x.Status.Epic.Space.Id,
                SpaceKey = x.Status.Epic.Space.Key,
                SpaceName = x.Status.Epic.Space.Name,
                SpaceColor = x.Status.Epic.Space.Color,
            })
            .FirstAsyncEF(cancellationToken);

        var attributeValues = await context.Attributes
            .Where(x => x.OrganizationId == result.OrganizationId)
            .Select(x => new DetailIssueAttributeDto
            {
                Id = x.Id,
                Type = x.AttributeType,
                Name = x.Name,
                ListValues = x.AttributeListValues!
                    .Select(v => new IssueAttributeListValueDto
                    {
                        Name = v.Value,
                        Id = v.Id,
                    })
                    .ToArray(),
                Value = string.Empty, // Fills via mapping
                Color = x.Color,
            })
            .ToArrayAsyncEF(cancellationToken);

        var attributeValuesResult = await GetIssueAttributeValues(issueId, cancellationToken);
        foreach (var attributeValue in attributeValues)
        {
            if (attributeValuesResult.TryGetValue(attributeValue.Id, out var value))
                attributeValue.Value = value;
        }

        var media = await GetAttachments(result.Id, cancellationToken);

        return new IssueDetailDto
        {
            Id = result.Id,
            AssigneeId = result.AssigneeId,
            Assignee = new IssueAssigneeDetails
            {
                Color = result.AssigneeColor,
                DisplayName = result.AssigneeDisplayName,
                Initials = result.AssigneeInitials,
                IsCurrentUser = result.AssigneeId == request.AuthData.UserId,
            },
            Content = result.Content,
            Owner = new UserDetails
            {
                Color = result.OwnerColor,
                DisplayName = result.OwnerDisplayName,
                Initials = result.OwnerInitials,
            },
            Time = result.Time,
            UpdatedAt = result.UpdatedAt,
            EpicId = result.CategoryId,
            EpicName = result.CategoryName,
            StatusId = result.StatusId,
            StatusName = result.StatusName,
            EpicColor = result.CategoryColor,
            StatusColor = result.StatusColor,
            CanEdit = issueAccessLevels.CanUpdateIssue,
            AttributeValues = attributeValues,
            Key = $"{result.SpaceKey}-{result.Number}",
            SpaceKey = result.SpaceKey,
            SpaceName = result.SpaceName,
            SpaceColor = result.SpaceColor,
            Attachments = media,
        };
    }

    private Task<List<AttachmentData>> GetAttachments(long issueId, CancellationToken ct)
    {
        return context
            .IssueAttachments
            .Where(x => issueId == x.IssueId)
            .Select(x => new AttachmentData
            {
                Id = x.AttachmentId,
                Type = x.Attachment!.Type,
                OriginalFileId = x.Attachment.FileId,
                PreviewFileId = x.Attachment.PreviewFileId,
                FileName = x.Attachment.File!.Name,
            })
            .ToListAsyncEF(ct);
    }

    private Task<long> GetIssueIdByIssueKey(
        long organizationId,
        IssueKey issueKey,
        CancellationToken cancellationToken)
    {
        return context.IssueNumbers
            .Where(x => x.Number == issueKey.Number)
            .Where(x => x.Space!.Key == issueKey.SpaceKey)
            .Where(x => x.Space!.OrganizationId == organizationId)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync($"Issue: {issueKey} is not found in organization", cancellationToken);
    }

    public async Task<long> AddIssueComment(AddCommentRequest request, CancellationToken cancellationToken)
    {
        var issueKey = new IssueKey(request.IssueKey);
        var issueId = await GetIssueIdIfAccessible(
            request.AuthData,
            issueKey,
            x => x.CanUpdateIssue,
            cancellationToken);
        
        if (FilesHasError(request.Files, out var error))
            throw new BadRequestException(nameof(request.Files), error);
        
        var uploadedFiles = await UploadFiles(request.Files, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        
        var commentId = await issuesService.AddComment(
            issueId,
            request.AuthData.UserId,
            request.Text,
            uploadedFiles,
            cancellationToken);
        
        await transaction.CommitAsync(cancellationToken);
        
        return commentId;
    }

    public async Task UpdateIssueComment(
        UpdateCommentRequest request,
        CancellationToken cancellationToken)
    {
        var comment = await context.IssueComments
            .Where(x => x.Id == request.CommentId)
            .Select(x => new
            {
                x.Id,
                x.OwnerId,
                x.IssueId,
            })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (comment?.OwnerId != request.AuthData.UserId)
            throw new ForbiddenException(string.Format(ErrorMessages.CommentNotAvailableToEdit, request.CommentId));
        
        if (FilesHasError(request.AddFiles, out var error))
            throw new BadRequestException(nameof(request.AddFiles), error);
        
        var uploadedFiles = await UploadFiles(request.AddFiles, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await issuesService.UpdateComment(
            comment.Id,
            comment.OwnerId,
            request.Text,
            uploadedFiles,
            request.RemoveAttachmentIds,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteIssueComment(DeleteCommentRequest request, CancellationToken cancellationToken)
    {
        var entity = await context.IssueComments
            .Where(x => x.Id == request.CommentId)
            .Select(x => new
            {
                x.Id,
                x.OwnerId,
            })
            .FirstOrDefaultAsyncEF(cancellationToken);
        
        if (entity?.OwnerId != request.AuthData.UserId)
            throw new ForbiddenException(string.Format(ErrorMessages.CommentNotAvailableToDelete, request.CommentId));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await issuesService.DeleteComment(request.CommentId, request.AuthData.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ChangesIssuesOrder(ChangesIssuesOrderRequest request, CancellationToken ct)
    {
        var targetIssueId = await GetIssueIdIfAccessible(
            request.AuthData,
            new IssueKey(request.TargetKey),
            x => x.CanRead,
            ct);

        var issueIds = new List<long>();
        foreach (var issueKey in request.IssueKeys) // TODO - BRD-146 get rid of O(n)
        {
            var issueToMoveId = await GetIssueIdIfAccessible(
                request.AuthData,
                new IssueKey(issueKey),
                x => x.CanUpdateIssue,
                ct);
            
            issueIds.Add(issueToMoveId);
        }
        
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        await issuesService.UpdateIssuesOrder(
            issueIds.ToArray(),
            targetIssueId,
            request.TargetType,
            ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<Dictionary<string, string>> UpdateIssuesStatus(UpdateIssuesStatusRequest request, CancellationToken ct)
    {
        // Check that can move Issues
        var issueIds = new List<long>();
        foreach (var issueKey in request.IssueKeys) // TODO - BRD-146 get rid of O(n)
        {
            var issueToMoveId = await GetIssueIdIfAccessible(
                request.AuthData,
                new IssueKey(issueKey),
                x => x.CanUpdateIssue,
                ct);
            
            issueIds.Add(issueToMoveId);
        }
        
        // Check that can move to specified status
        var canMove = await accessService.CanMoveToStatus(
            request.AuthData,
            request.StatusId,
            ct);
        
        if (!canMove)
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "Status", request.StatusId));
        
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var result = await issuesService.UpdateIssuesStatus(
            issueIds.ToArray(),
            request.StatusId,
            request.AuthData.UserId,
            ct);
        await transaction.CommitAsync(ct);

        return result;
    }

    public async Task<ShortPaginatedResult<CommentDto>> GetIssueComments(
        GetIssueCommentsRequest request,
        CancellationToken ct)
    {
        var issueId = await GetIssueIdByIssueKey(
            request.AuthData.OrganizationId,
            new IssueKey(request.IssueKey),
            ct);
        
        await accessService.GetAccessLevelsByIssueId(request.AuthData, issueId, ct)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", request.IssueKey))
            .EnsureOrThrowNotFound(a => a.CanRead, string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", request.IssueKey));

        var commentsData = await context
            .IssueComments
            .Where(x => x.IssueId == issueId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Text,
                x.Id,
                x.CreatedAt,
                x.UpdatedAt,
                x.Owner!.Color,
                x.Owner.DisplayName,
                x.Owner.Initials,
                CanModify = x.OwnerId == request.AuthData.UserId,
                Attachments = x.Attachments
                    .Select(a => new AttachmentData
                    {
                        Id = a.AttachmentId,
                        OriginalFileId = a.Attachment!.FileId,
                        PreviewFileId = a.Attachment.PreviewFileId,
                        Type = a.Attachment.Type,
                        FileName = a.Attachment.File!.Name,
                    })
                    .ToList(),
            })
            .ShortPaginateEFAsync(request.Pagination, ct);

        var result = commentsData.MapTo(item => new CommentDto
        {
            Id = item.Id,
            Text = item.Text,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            CanModify = item.CanModify,
            Owner = new UserDetails
            {
                Color = item.Color,
                DisplayName = item.DisplayName,
                Initials = item.Initials,
            },
            Attachments = item.Attachments,
        });

        return result;
    }

    private async Task<long> GetIssueIdIfAccessible(
        OrganizationAuthData authData,
        IssueKey issueKey,
        Func<AccessLevels, bool> isAccessible,
        CancellationToken cancellationToken)
    {
        var issueId = await GetIssueIdByIssueKey(
            authData.OrganizationId,
            issueKey,
            cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", issueKey))
            .EnsureOrThrowForbidden(isAccessible, $"Issue: {issueKey} is not available for this action");

        return issueId;
    }

    private async Task<MediaInfo[]> UploadFiles(IFormFile[] formFiles, CancellationToken cancellationToken)
    {
        var files = new List<MediaInfo>();
        foreach (var formFile in formFiles)
        {
            var fileData = await coreFilesService.UploadFile(
                formFile.FileName, 
                formFile.ContentType,
                formFile.OpenReadStream(),
                cancellationToken);
            
            files.Add(fileData);
        }
        
        return files.ToArray();
    }

    private async Task<SetIssueAttributeRequest[]> GetAttributeUpdateRequests(
        long organizationId,
        AttributeValue[] attributeValues,
        CancellationToken ct)
    {
        if (attributeValues.Length == 0)
            return [];

        var uniqueValues = attributeValues
            .DistinctBy(x => x.AttributeId)
            .ToArray();
        
        var requests = new List<SetIssueAttributeRequest>();
        var attributeValidationErrors = new List<string>();
        
        var attributes = await context.Attributes
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => uniqueValues.Select(v => v.AttributeId).Contains(x.Id))
            .Select(x => new { x.Id, x.AttributeType })
            .ToDictionaryAsyncEF(x => x.Id, x => x.AttributeType, ct);

        foreach (var attribute in uniqueValues)
        {
            if (!attributes.TryGetValue(attribute.AttributeId, out var attributeType))
                attributeValidationErrors.Add(string.Format(ErrorMessages.EntityNotFound, "Attribute", attribute.AttributeId));

            switch (attributeType)
            {
                case AttributeType.List:
                {
                    if (attribute is not EnumAttributeValue enumAttributeValue)
                    {
                        attributeValidationErrors.Add(string.Format(ErrorMessages.AttributeShouldBeEnum, attribute.AttributeId));
                        continue;
                    }
                    
                    requests.Add(
                        new SetIssueListAttributeRequest
                        {
                            Id = enumAttributeValue.AttributeId,
                            ListValueId = enumAttributeValue.ValueId
                        });
                    break;
                }
                case AttributeType.Text:
                {
                    if (attribute is not StringAttributeValue stringAttributeValue)
                    {
                        attributeValidationErrors.Add(string.Format(ErrorMessages.AttributeShouldBeString, attribute.AttributeId));
                        continue;
                    }

                    if (stringAttributeValue.Value.Length > 255)
                    {
                        attributeValidationErrors.Add(string.Format(ErrorMessages.AttributeStringTooLong, attribute.AttributeId));
                        continue;
                    }
                    
                    requests.Add(
                        new SetIssueTextAttributeRequest
                        {
                            Id = stringAttributeValue.AttributeId,
                            Value = stringAttributeValue.Value,
                        });
                    break;
                }
                case AttributeType.Integer:
                {
                    if (attribute is not IntegerAttributeValue integerAttributeValue)
                    {
                        attributeValidationErrors.Add(string.Format(ErrorMessages.AttributeShouldBeInteger, attribute.AttributeId));
                        continue;
                    }

                    requests.Add(
                        new SetIssueIntegerAttributeRequest
                        {
                            Id = integerAttributeValue.AttributeId,
                            Value = integerAttributeValue.Value,
                        });
                    break;
                }
                case AttributeType.Decimal:
                {
                    if (attribute is not DecimalAttributeValue decimalAttributeValue)
                    {
                        attributeValidationErrors.Add(string.Format(ErrorMessages.AttributeShouldBeDecimal, attribute.AttributeId));
                        continue;
                    }

                    requests.Add(
                        new SetIssueDecimalAttributeRequest
                        {
                            Id = decimalAttributeValue.AttributeId,
                            Value = decimalAttributeValue.Value,
                        });
                    break;
                }
                case AttributeType.Date:
                {
                    if (attribute is not DateAttributeValue dateAttributeValue)
                    {
                        attributeValidationErrors.Add(string.Format(ErrorMessages.AttributeShouldBeDate, attribute.AttributeId));
                        continue;
                    }

                    requests.Add(
                        new SetIssueDateAttributeRequest
                        {
                            Id = dateAttributeValue.AttributeId,
                            Value = dateAttributeValue.Value,
                        });
                    break;
                }
                case AttributeType.DateTime:
                {
                    if (attribute is not DateTimeAttributeValue dateTimeAttributeValue)
                    {
                        attributeValidationErrors.Add(string.Format(ErrorMessages.AttributeShouldBeDateTime, attribute.AttributeId));
                        continue;
                    }

                    requests.Add(
                        new SetIssueDateTimeAttributeRequest
                        {
                            Id = dateTimeAttributeValue.AttributeId,
                            Value = dateTimeAttributeValue.Value,
                        });
                    break;
                }

                default:
                    throw new InvalidOperationException($"Attribute type {attributeType} is not supported");
            }
        }

        if (attributeValidationErrors.Count > 0)
            throw new BadRequestException(new Dictionary<string, string?[]>
            {
                [nameof(attributeValues)] = attributeValidationErrors.ToArray(),
            });

        return requests.ToArray();
    }

    private async Task<List<SearchIssueDto>> MapToSearchDtos(
        OrganizationAuthData authData,
        IList<IssueListDto> elements,
        CancellationToken ct)
    {
        var spaceKeys = elements.Select(y => y.SpaceKey).Distinct().ToArray();
        var spaces = await context.Spaces
            .Where(x => x.OrganizationId == authData.OrganizationId)
            .Where(x => spaceKeys.Contains(x.Key))
            .ToDictionaryAsyncEF(
                x => x.Key,
                x => new NameAndColor
                {
                    Name = x.Name,
                    Color = x.Color,
                }, ct);
        
        var epics = await context.Epics
            .Where(x => elements.Select(y => y.EpicId).Distinct().Contains(x.Id))
            .ToDictionaryAsyncEF(
                x => x.Id,
                x => new NameAndColor
                {
                    Name = x.Name,
                    Color = x.Color,
                }, ct);
        
        var statuses = await context.Statuses
            .Where(x => elements.Select(y => y.StatusId).Distinct().Contains(x.Id))
            .Where(x => !x.Epic!.IsDefault)
            .ToDictionaryAsyncEF(
                x => x.Id,
                x => new NameAndColor
                {
                    Name = x.Name,
                    Color = x.Color,
                }, ct);

        var spacesWithAllowedUpdate = (await accessService.GetSpacesWithAllowedIssuesUpdate(
            authData,
            query => query
                .Where(x => spaces.Keys.Contains(x.Key))
                .Select(x => x.Key)
                .ToArrayAsyncEF(ct),
            ct))
            .ToHashSet();
        
        var result = new List<SearchIssueDto>();

        foreach (var element in elements)
        {
            result.Add(new SearchIssueDto
            {
                EpicId = element.EpicId,
                Epic = epics[element.EpicId],
                StatusId = element.StatusId,
                Status = statuses.GetValueOrDefault(element.StatusId),
                SpaceKey = element.SpaceKey,
                Space = spaces[element.SpaceKey],
                Id = element.Id,
                Content = element.Content,
                Key = element.Key,
                Assignee = element.Assignee,
                AssigneeColor = element.AssigneeColor,
                Time = element.Time,
                AssigneeInitial = element.AssigneeInitial,
                Attributes = element.Attributes,
                CanEdit = spacesWithAllowedUpdate.Contains(element.SpaceKey),
            });
        }
        
        return result;
    }

    private async Task<Dictionary<long, Dictionary<long, string>>> GetIssuesAttributeValues(
        IEnumerable<long> issueIds,
        CancellationToken cancellationToken)
    {
        var ids = issueIds as long[] ?? issueIds.ToArray();

        var dbResult = await GetScalarAttributeDisplayValues(ids, cancellationToken);

        return dbResult
            .GroupBy(x => x.IssueId)
            .ToDictionary(
                x => x.Key,
                x => x.ToDictionary(
                    y => y.AttributeId,
                    y => y.Value));
    }

    /// <summary>
    /// Loads every attribute value (of any <see cref="AttributeType"/>) for the given issues as a
    /// flat, opaque display string per (issue, attribute) pair. Each type is queried against its
    /// own table (see <see cref="Laraue.Apps.Boards.DataAccess.Models.IIssueAttributeScalarValue{TValue}"/>)
    /// and formatted client-side rather than via a single SQL UNION, since not every provider
    /// reliably translates e.g. <c>decimal</c>/<c>DateTime</c> ToString() to SQL.
    /// </summary>
    private async Task<(long IssueId, long AttributeId, string Value)[]> GetScalarAttributeDisplayValues(
        long[] issueIds,
        CancellationToken cancellationToken)
    {
        var textValues = await context.IssueAttributeTextValues
            .Where(x => issueIds.Contains(x.IssueId))
            .Select(x => new { x.IssueId, x.AttributeId, x.Value })
            .ToArrayAsyncEF(cancellationToken);

        var listValues = await context.IssueAttributeListValues
            .Where(x => issueIds.Contains(x.IssueId))
            .Select(x => new { x.IssueId, x.AttributeId, x.AttributeListValueId })
            .ToArrayAsyncEF(cancellationToken);

        var integerValues = await context.IssueAttributeIntegerValues
            .Where(x => issueIds.Contains(x.IssueId))
            .Select(x => new { x.IssueId, x.AttributeId, x.Value })
            .ToArrayAsyncEF(cancellationToken);

        var decimalValues = await context.IssueAttributeDecimalValues
            .Where(x => issueIds.Contains(x.IssueId))
            .Select(x => new { x.IssueId, x.AttributeId, x.Value })
            .ToArrayAsyncEF(cancellationToken);

        var dateValues = await context.IssueAttributeDateValues
            .Where(x => issueIds.Contains(x.IssueId))
            .Select(x => new { x.IssueId, x.AttributeId, x.Value })
            .ToArrayAsyncEF(cancellationToken);

        var dateTimeValues = await context.IssueAttributeDateTimeValues
            .Where(x => issueIds.Contains(x.IssueId))
            .Select(x => new { x.IssueId, x.AttributeId, x.Value })
            .ToArrayAsyncEF(cancellationToken);

        return textValues
            .Select(x => (x.IssueId, x.AttributeId, x.Value))
            .Concat(listValues.Select(x => (x.IssueId, x.AttributeId, Value: x.AttributeListValueId.ToString())))
            .Concat(integerValues.Select(x => (x.IssueId, x.AttributeId, Value: x.Value.ToString(CultureInfo.InvariantCulture))))
            .Concat(decimalValues.Select(x => (x.IssueId, x.AttributeId, Value: x.Value.ToString("0.####", CultureInfo.InvariantCulture))))
            .Concat(dateValues.Select(x => (x.IssueId, x.AttributeId, Value: x.Value.ToString("O"))))
            .Concat(dateTimeValues.Select(x => (x.IssueId, x.AttributeId, Value: x.Value.ToString("O"))))
            .ToArray();
    }

    private async Task<Dictionary<long, string>> GetIssueAttributeValues(
        long issueId,
        CancellationToken cancellationToken)
    {
        var result = await GetIssuesAttributeValues([issueId], cancellationToken);

        return result.GetValueOrDefault(issueId, new Dictionary<long, string>());
    }

    private async Task EnrichAttributes(IList<IssueListDto> elements, CancellationToken ct)
    {
        var ids = elements.Select(x => x.Id).ToArray();

        var textValues = await context.IssueAttributeTextValues
            .Where(x => ((IEnumerable<long>)ids).Contains(x.IssueId))
            .Select(x => new { x.Attribute!.Color, x.IssueId, x.Value })
            .ToArrayAsyncEF(ct);

        var listValues = await context.IssueAttributeListValues
            .Where(x => ((IEnumerable<long>)ids).Contains(x.IssueId))
            .Select(x => new { x.Attribute!.Color, x.IssueId, x.AttributeListValue!.Value })
            .ToArrayAsyncEF(ct);

        var integerValues = await context.IssueAttributeIntegerValues
            .Where(x => ((IEnumerable<long>)ids).Contains(x.IssueId))
            .Select(x => new { x.Attribute!.Color, x.IssueId, x.Value })
            .ToArrayAsyncEF(ct);

        var decimalValues = await context.IssueAttributeDecimalValues
            .Where(x => ((IEnumerable<long>)ids).Contains(x.IssueId))
            .Select(x => new { x.Attribute!.Color, x.IssueId, x.Value })
            .ToArrayAsyncEF(ct);

        var dateValues = await context.IssueAttributeDateValues
            .Where(x => ((IEnumerable<long>)ids).Contains(x.IssueId))
            .Select(x => new { x.Attribute!.Color, x.IssueId, x.Value })
            .ToArrayAsyncEF(ct);

        var dateTimeValues = await context.IssueAttributeDateTimeValues
            .Where(x => ((IEnumerable<long>)ids).Contains(x.IssueId))
            .Select(x => new { x.Attribute!.Color, x.IssueId, x.Value })
            .ToArrayAsyncEF(ct);

        var all = textValues
            .Union(listValues)
            .Union(integerValues.Select(x => new { x.Color, x.IssueId, Value = x.Value.ToString(CultureInfo.InvariantCulture) }))
            .Union(decimalValues.Select(x => new { x.Color, x.IssueId, Value = x.Value.ToString("0.####", CultureInfo.InvariantCulture) }))
            .Union(dateValues.Select(x => new { x.Color, x.IssueId, Value = x.Value.ToString("O") }))
            .Union(dateTimeValues.Select(x => new { x.Color, x.IssueId, Value = x.Value.ToString("O") }))
            .GroupBy(x => x.IssueId)
            .ToDictionary(x => x.Key);

        foreach (var element in elements)
        {
            if (all.TryGetValue(element.Id, out var attributes))
            {
                foreach (var attribute in attributes)
                {
                    element.Attributes.Add(new IssueListAttributeDto
                    {
                        Value = attribute.Value,
                        Color = attribute.Color,
                    });
                }
            }
        }
    }

    private static IQueryable<IssueListDtoData> ProjectToTemporaryDto(
        IQueryable<Issue> queryable)
    {
        return queryable.Select(x => new IssueListDtoData
        {
            Id = x.Id,
            Content = x.Content,
            Time = x.CreatedAt,
            EpicId = x.Status!.EpicId,
            StatusId = x.StatusId,
            AssigneeDisplayName = x.Assignee!.DisplayName,
            AssigneeInitials = x.Assignee.Initials,
            AssigneeTelegramId = x.Assignee.TelegramId,
            AssigneeUserColor = x.Assignee.Color,
            Number = x.IssueNumber!.Number,
            SpaceKey = x.Status.Epic!.Space!.Key,
            SpaceId = x.Status.Epic.SpaceId
        });
    }
    
    private static IssueListDto Map(IssueListDtoData source)
    {
        return new IssueListDto
        {
            Id = source.Id,
            StatusId = source.StatusId,
            Content = source.Content,
            EpicId = source.EpicId,
            Assignee = source.AssigneeDisplayName,
            AssigneeInitial = source.AssigneeInitials,
            Time = source.Time,
            AssigneeColor = source.AssigneeUserColor,
            Key = new IssueKey(source.SpaceKey, source.Number).ToString(),
            SpaceKey = source.SpaceKey,
        };
    }
    
    private async Task<IQueryable<Issue>> ApplyFilters(
        IQueryable<Issue> query,
        IHasAttributeFilters request,
        CancellationToken cancellationToken = default)
    {
        if (request.Filters.Count == 0)
            return query;

        var filterTypes = await GetAllowedOrganizationAttributesQuery(request.AuthData)
            .Where(x => request.Filters.Keys.Any(y => y == x.Id))
            .ToDictionaryAsyncEF(x => x.Id, x => x.AttributeType, cancellationToken);

        var errors = new Dictionary<long, string>();
        
        foreach (var filter in request.Filters)
        {
            if (!filterTypes.TryGetValue(filter.Key, out var filterType))
            {
                errors.Add(filter.Key, string.Format(ErrorMessages.FilterNotFound, filter.Key));
                continue;
            }

            query = filterType switch
            {
                AttributeType.Text => ApplyTextFilter(query, filter, errors),
                AttributeType.List => ApplyEnumFilter(query, filter, errors),
                AttributeType.Integer => ApplyIntegerFilter(query, filter, errors),
                AttributeType.Decimal => ApplyDecimalFilter(query, filter, errors),
                AttributeType.Date => ApplyDateFilter(query, filter, errors),
                AttributeType.DateTime => ApplyDateTimeFilter(query, filter, errors),
                _ => throw new InvalidOperationException($"Unsupported filter type '{filterType}'")
            };
        }

        if (errors.Count != 0)
            throw new BadRequestException(new Dictionary<string, string?[]>
            {
                [nameof(request.Filters)] = errors.Select(x => $"{x.Key}: {x.Value}").ToArray()
            });

        return query;
    }

    private IQueryable<Attribute> GetAllowedOrganizationAttributesQuery(OrganizationAuthData authData)
    {
        return context.Attributes
            .Where(x => x.OrganizationId == authData.OrganizationId);
    }

    private IQueryable<Issue> ApplyTextFilter(
        IQueryable<Issue> query,
        KeyValuePair<long, AttributeFilterValue> filter,
        Dictionary<long, string> errors)
    {
        if (filter.Value is not StringAttributeFilterValue stringValue)
        {
            errors.Add(filter.Key, string.Format(ErrorMessages.StringFilterExpected, filter.Key));
            return query;
        }

        if (string.IsNullOrEmpty(stringValue.SearchString))
            return query;
                
        return query.InnerJoin(
            context.IssueAttributeTextValues,
            (i, a) => i.Id == a.IssueId
                      && a.AttributeId == filter.Key
                      && a.Value.ILike(stringValue.SearchString.AsSearchable()),
            (i, a) => i);
    }
    
    private IQueryable<Issue> ApplyEnumFilter(
        IQueryable<Issue> query,
        KeyValuePair<long, AttributeFilterValue> filter,
        Dictionary<long, string> errors)
    {
        if (filter.Value is not EnumAttributeFilterValue enumValue)
        {
            errors.Add(filter.Key, string.Format(ErrorMessages.EnumFilterExpected, filter.Key));
            return query;
        }

        if (enumValue.Ids.Length == 0)
            return query;
                
        return query.InnerJoin(
            context.IssueAttributeListValues,
            (i, a) => i.Id == a.IssueId && a.AttributeId == filter.Key && ((IEnumerable<long>)enumValue.Ids).Contains(a.AttributeListValueId),
            (i, a) => i);
    }

    private IQueryable<Issue> ApplyIntegerFilter(
        IQueryable<Issue> query,
        KeyValuePair<long, AttributeFilterValue> filter,
        Dictionary<long, string> errors)
    {
        if (filter.Value is not IntegerAttributeFilterValue integerValue)
        {
            errors.Add(filter.Key, string.Format(ErrorMessages.IntegerFilterExpected, filter.Key));
            return query;
        }

        if (integerValue.Min is null && integerValue.Max is null)
            return query;

        return query.InnerJoin(
            context.IssueAttributeIntegerValues,
            (i, a) => i.Id == a.IssueId
                      && a.AttributeId == filter.Key
                      && (integerValue.Min == null || a.Value >= integerValue.Min)
                      && (integerValue.Max == null || a.Value <= integerValue.Max),
            (i, a) => i);
    }

    private IQueryable<Issue> ApplyDecimalFilter(
        IQueryable<Issue> query,
        KeyValuePair<long, AttributeFilterValue> filter,
        Dictionary<long, string> errors)
    {
        if (filter.Value is not DecimalAttributeFilterValue decimalValue)
        {
            errors.Add(filter.Key, string.Format(ErrorMessages.DecimalFilterExpected, filter.Key));
            return query;
        }

        if (decimalValue.Min is null && decimalValue.Max is null)
            return query;

        return query.InnerJoin(
            context.IssueAttributeDecimalValues,
            (i, a) => i.Id == a.IssueId
                      && a.AttributeId == filter.Key
                      && (decimalValue.Min == null || a.Value >= decimalValue.Min)
                      && (decimalValue.Max == null || a.Value <= decimalValue.Max),
            (i, a) => i);
    }

    private IQueryable<Issue> ApplyDateFilter(
        IQueryable<Issue> query,
        KeyValuePair<long, AttributeFilterValue> filter,
        Dictionary<long, string> errors)
    {
        if (filter.Value is not DateAttributeFilterValue dateValue)
        {
            errors.Add(filter.Key, string.Format(ErrorMessages.DateFilterExpected, filter.Key));
            return query;
        }

        if (dateValue.From is null && dateValue.To is null)
            return query;

        return query.InnerJoin(
            context.IssueAttributeDateValues,
            (i, a) => i.Id == a.IssueId
                      && a.AttributeId == filter.Key
                      && (dateValue.From == null || a.Value >= dateValue.From)
                      && (dateValue.To == null || a.Value <= dateValue.To),
            (i, a) => i);
    }

    private IQueryable<Issue> ApplyDateTimeFilter(
        IQueryable<Issue> query,
        KeyValuePair<long, AttributeFilterValue> filter,
        Dictionary<long, string> errors)
    {
        if (filter.Value is not DateTimeAttributeFilterValue dateTimeValue)
        {
            errors.Add(filter.Key, string.Format(ErrorMessages.DateTimeFilterExpected, filter.Key));
            return query;
        }

        if (dateTimeValue.From is null && dateTimeValue.To is null)
            return query;

        return query.InnerJoin(
            context.IssueAttributeDateTimeValues,
            (i, a) => i.Id == a.IssueId
                      && a.AttributeId == filter.Key
                      && (dateTimeValue.From == null || a.Value >= dateTimeValue.From)
                      && (dateTimeValue.To == null || a.Value <= dateTimeValue.To),
            (i, a) => i);
    }

    private Task<IQueryable<Issue>> ApplySorting(
        IQueryable<Issue> query,
        IHasSorting request,
        CancellationToken cancellationToken = default)
    {
        return request.Sorting switch
        {
            null =>
                Task.FromResult<IQueryable<Issue>>(query.OrderBy(x => x.LexoRank)),
            ByAttributeIssueSorting byAttributeIssueSorting =>
                ApplyByAttributeSorting(query, byAttributeIssueSorting, request.AuthData, cancellationToken),
            ByPropertyIssueSorting byPropertyIssueSorting =>
                Task.FromResult(ApplyByPropertySorting(query, byPropertyIssueSorting)),
            _ =>
                throw new InvalidOperationException($"Unsupported sorting type '{request.Sorting}'")
        };
    }

    private async Task<IQueryable<Issue>> ApplyByAttributeSorting(
        IQueryable<Issue> query,
        ByAttributeIssueSorting sorting,
        OrganizationAuthData authData,
        CancellationToken cancellationToken = default)
    {
        var attribute = await GetAllowedOrganizationAttributesQuery(authData)
            .Where(x => x.Id == sorting.AttributeId)
            .Select(x => new { x.AttributeType })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (attribute is null)
            throw new BadRequestException(
                nameof(IHasSorting.Sorting),
                string.Format(ErrorMessages.EntityNotFound, "Attribute", sorting.AttributeId));

        return attribute.AttributeType switch
        {
            AttributeType.Text => ApplyTextSorting(query, sorting),
            AttributeType.List => ApplyEnumSorting(query, sorting),
            AttributeType.Integer => ApplyIntegerSorting(query, sorting),
            AttributeType.Decimal => ApplyDecimalSorting(query, sorting),
            AttributeType.Date => ApplyDateSorting(query, sorting),
            AttributeType.DateTime => ApplyDateTimeSorting(query, sorting),
            _ => throw new InvalidOperationException($"Sorting by '{attribute.AttributeType}' is not supported")
        };
    }
    
    private IQueryable<Issue> ApplyTextSorting(
        IQueryable<Issue> query,
        ByAttributeIssueSorting sorting)
    {
        return query
            .InnerJoin(
                context.IssueAttributeTextValues,
                (issue, textValue) => issue.Id == textValue.IssueId && textValue.AttributeId == sorting.AttributeId,
                (issue, textValue) => new { Issue = issue, TextValue = textValue })
            .ApplySorting(a => a.TextValue.Value, sorting.Direction)
            .Select(a => a.Issue);
    }
    
    private IQueryable<Issue> ApplyEnumSorting(
        IQueryable<Issue> query,
        ByAttributeIssueSorting sorting)
    {
        return query
            .InnerJoin(
                context.IssueAttributeListValues,
                (issue, listValue) => issue.Id == listValue.IssueId && listValue.AttributeId == sorting.AttributeId,
                (issue, listValue) => new { Issue = issue, ListValue = listValue })
            .ApplySorting(a => a.ListValue.AttributeListValueId, sorting.Direction)
            .Select(a => a.Issue);
    }

    private IQueryable<Issue> ApplyIntegerSorting(
        IQueryable<Issue> query,
        ByAttributeIssueSorting sorting)
    {
        return query
            .InnerJoin(
                context.IssueAttributeIntegerValues,
                (issue, integerValue) => issue.Id == integerValue.IssueId && integerValue.AttributeId == sorting.AttributeId,
                (issue, integerValue) => new { Issue = issue, IntegerValue = integerValue })
            .ApplySorting(a => a.IntegerValue.Value, sorting.Direction)
            .Select(a => a.Issue);
    }

    private IQueryable<Issue> ApplyDecimalSorting(
        IQueryable<Issue> query,
        ByAttributeIssueSorting sorting)
    {
        return query
            .InnerJoin(
                context.IssueAttributeDecimalValues,
                (issue, decimalValue) => issue.Id == decimalValue.IssueId && decimalValue.AttributeId == sorting.AttributeId,
                (issue, decimalValue) => new { Issue = issue, DecimalValue = decimalValue })
            .ApplySorting(a => a.DecimalValue.Value, sorting.Direction)
            .Select(a => a.Issue);
    }

    private IQueryable<Issue> ApplyDateSorting(
        IQueryable<Issue> query,
        ByAttributeIssueSorting sorting)
    {
        return query
            .InnerJoin(
                context.IssueAttributeDateValues,
                (issue, dateValue) => issue.Id == dateValue.IssueId && dateValue.AttributeId == sorting.AttributeId,
                (issue, dateValue) => new { Issue = issue, DateValue = dateValue })
            .ApplySorting(a => a.DateValue.Value, sorting.Direction)
            .Select(a => a.Issue);
    }

    private IQueryable<Issue> ApplyDateTimeSorting(
        IQueryable<Issue> query,
        ByAttributeIssueSorting sorting)
    {
        return query
            .InnerJoin(
                context.IssueAttributeDateTimeValues,
                (issue, dateTimeValue) => issue.Id == dateTimeValue.IssueId && dateTimeValue.AttributeId == sorting.AttributeId,
                (issue, dateTimeValue) => new { Issue = issue, DateTimeValue = dateTimeValue })
            .ApplySorting(a => a.DateTimeValue.Value, sorting.Direction)
            .Select(a => a.Issue);
    }

    private IQueryable<Issue> ApplyByPropertySorting(
        IQueryable<Issue> query,
        ByPropertyIssueSorting sorting)
    {
        return sorting.Property switch
        {
            IssueProperty.CreatedAt => query.ApplySorting(x => x.CreatedAt, sorting.Direction),
            IssueProperty.UpdatedAt => query.ApplySorting(x => x.UpdatedAt, sorting.Direction),
            IssueProperty.Content => query.ApplySorting(x => x.Content, sorting.Direction),
            _ => throw new InvalidOperationException($"Sorting by '{sorting.Property}' is not supported")
        };
    }
}

public record GetIssuesRequest : BatchRequest, IHasAttributeFilters, IHasSorting
{
    public OrganizationAuthData AuthData { get; set; }
    public long StatusId { get; set; }
    public string? SearchString { get; set; }
    public Dictionary<long, AttributeFilterValue> Filters { get; set; } = new();
    public IssueSorting? Sorting { get; set; }
}

public record GetIssueRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required IssueKey IssueKey { get; set; }
}

public record GetBoardRequest : IHasAttributeFilters, IHasSorting
{
    public OrganizationAuthData AuthData { get; set; }
    public required long EpicId { get; set; }
    
    [Range(1, 100)]
    public required int Take { get; init; }
    public string? SearchString { get; init; }
    public Dictionary<long, AttributeFilterValue> Filters { get; set; } = new();
    public IssueSorting? Sorting { get; set; }
}

public record GetBoardSummaryRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required string SpaceKey { get; set; }
}

public record SummarizeIssueContentRequest
{
    public OrganizationAuthData AuthData { get; set; }

    [MaxLength(4096)]
    public required string Content { get; set; }
}

public record ColumnIssues
{
    public required long StatusId { get; set; }
    public required InitialBatchResult<IssueListDto> Items { get; set; }
}

public class IssueListDtoData
{
    public required long Id { get; set; }
    public required DateTime Time { get; set; }
    public required long AssigneeTelegramId { get; set; }
    public required string AssigneeDisplayName { get; set; }
    public required string AssigneeInitials { get; set; }
    public required string? Content { get; set; }
    public required string AssigneeUserColor { get; set; }
    public required long EpicId { get; set; }
    public required long StatusId { get; set; }
    public required int Number { get; set; }
    public required string SpaceKey { get; set; }
    public required long SpaceId { get; set; }
}

public record IssueListDto
{
    public required long Id { get; set; }
    public required DateTime Time { get; set; }
    public required string Assignee { get; set; }
    public required string Key { get; set; }
    public string? AssigneeInitial { get; set; }
    public required string AssigneeColor { get; set; }
    public required string? Content { get; set; }
    public required long EpicId { get; set; }
    public required long StatusId { get; set; }
    public required string SpaceKey { get; set; }
    public List<IssueListAttributeDto> Attributes { get; set; } = [];
}

public record IssueListAttributeDto
{
    public required string Value { get; set; }
    public required string Color { get; set; }
}

public record SearchIssueDto : IssueListDto
{
    public required NameAndColor Epic { get; set; }
    public required NameAndColor? Status { get; set; }
    public required NameAndColor Space { get; set; }
    public required bool CanEdit { get; set; }
}

public record NameAndColor
{
    public required string Name { get; set; }
    public required string Color { get; set; }
}

public record DeleteIssueRequest
{
    public required OrganizationAuthData AuthData { get; set; } = new();
    public required IssueKey IssueKey { get; set; }
}

public record CreateIssueRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public required long StatusId { get; set; }
    public required Guid AssigneeId { get; set; }
    public required string Content { get; set; }
    [JsonModelBinder]
    public AttributeValue[] AttributeValues { get; set; } = [];
    public IFormFile[] Files { get; set; } = [];
}

[JsonDerivedType(typeof(EnumAttributeValue), "enum")]
[JsonDerivedType(typeof(StringAttributeValue), "string")]
[JsonDerivedType(typeof(IntegerAttributeValue), "integer")]
[JsonDerivedType(typeof(DecimalAttributeValue), "decimal")]
[JsonDerivedType(typeof(DateAttributeValue), "date")]
[JsonDerivedType(typeof(DateTimeAttributeValue), "datetime")]
public abstract record AttributeValue
{
    public required long AttributeId { get; set; }
}

public record EnumAttributeValue : AttributeValue
{
    public required long ValueId { get; set; }
}

public record StringAttributeValue : AttributeValue
{
    public required string Value { get; set; }
}

public record IntegerAttributeValue : AttributeValue
{
    public required long Value { get; set; }
}

public record DecimalAttributeValue : AttributeValue
{
    public required decimal Value { get; set; }
}

public record DateAttributeValue : AttributeValue
{
    public required DateOnly Value { get; set; }
}

public record DateTimeAttributeValue : AttributeValue
{
    public required DateTime Value { get; set; }
}

public record UpdateIssueRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public IssueKey? IssueKey { get; set; }
    public required string Content { get; set; }
    public required Guid AssigneeId { get; set; }
    [JsonModelBinder]
    public AttributeValue[] AttributeValues { get; set; } = [];
    public Guid[] RemoveAttachmentIds { get; set; } = [];
    public IFormFile[] AddFiles { get; set; } = [];
}

public interface IHasAttributeFilters
{
    Dictionary<long, AttributeFilterValue> Filters { get; }
    public OrganizationAuthData AuthData { get; }
}

public interface IHasSorting
{
    IssueSorting? Sorting { get; }
    public OrganizationAuthData AuthData { get; }
}

[JsonDerivedType(typeof(StringAttributeFilterValue), "string")]
[JsonDerivedType(typeof(EnumAttributeFilterValue), "enum")]
[JsonDerivedType(typeof(IntegerAttributeFilterValue), "integer")]
[JsonDerivedType(typeof(DecimalAttributeFilterValue), "decimal")]
[JsonDerivedType(typeof(DateAttributeFilterValue), "date")]
[JsonDerivedType(typeof(DateTimeAttributeFilterValue), "datetime")]
public abstract record AttributeFilterValue
{
}

public record StringAttributeFilterValue : AttributeFilterValue
{
    /// <summary>
    /// String value to filter by.
    /// </summary>
    public required string SearchString { get; set; }
}

public record EnumAttributeFilterValue : AttributeFilterValue
{
    /// <summary>
    /// Enum identifiers to filter by.
    /// </summary>
    public required long[] Ids { get; set; }
}

public record IntegerAttributeFilterValue : AttributeFilterValue
{
    /// <summary>
    /// Inclusive lower bound. Null means no lower bound.
    /// </summary>
    public long? Min { get; set; }

    /// <summary>
    /// Inclusive upper bound. Null means no upper bound.
    /// </summary>
    public long? Max { get; set; }
}

public record DecimalAttributeFilterValue : AttributeFilterValue
{
    /// <summary>
    /// Inclusive lower bound. Null means no lower bound.
    /// </summary>
    public decimal? Min { get; set; }

    /// <summary>
    /// Inclusive upper bound. Null means no upper bound.
    /// </summary>
    public decimal? Max { get; set; }
}

public record DateAttributeFilterValue : AttributeFilterValue
{
    /// <summary>
    /// Inclusive lower bound. Null means no lower bound.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>
    /// Inclusive upper bound. Null means no upper bound.
    /// </summary>
    public DateOnly? To { get; set; }
}

public record DateTimeAttributeFilterValue : AttributeFilterValue
{
    /// <summary>
    /// Inclusive lower bound. Null means no lower bound.
    /// </summary>
    public DateTime? From { get; set; }

    /// <summary>
    /// Inclusive upper bound. Null means no upper bound.
    /// </summary>
    public DateTime? To { get; set; }
}

public record SearchRequest : IPaginationData, IHasAttributeFilters, IHasSorting
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long[] EpicIds { get; set; } = [];
    public EpicStatus[] EpicStatuses { get; set; } = [];
    public string[] SpaceKeys { get; set; } = [];
    public string? SearchString { get; set; }
    public required int Page { get; init; }
    public required int PerPage { get; init; }
    public Dictionary<long, AttributeFilterValue> Filters { get; set; } = new();
    public IssueSorting? Sorting { get; set; }
}

public class IssueDetailDto
{
    public required long Id { get; set; }
    public required Guid AssigneeId { get; set; }
    public required IssueAssigneeDetails Assignee { get; set; }
    public required DateTime Time { get; set; }
    public required DateTime UpdatedAt { get; set; }
    public required UserDetails Owner { get; set; }
    public required string? Content { get; set; }
    public required long EpicId { get; set; }
    public required string? EpicName { get; set; }
    public required string? EpicColor { get; set; }
    public required long StatusId { get; set; }
    public required string? StatusName { get; set; }
    public required string? StatusColor { get; set; }
    public required string SpaceKey { get; set; }
    public required string SpaceName { get; set; }
    public required string SpaceColor { get; set; }
    public required bool CanEdit { get; set; }
    public required string Key { get; set; }
    public required DetailIssueAttributeDto[] AttributeValues { get; set; }
    public required List<AttachmentData> Attachments { get; set; }
}

public record CommentDto
{
    public required long Id { get; set; }
    public required string Text { get; set; }
    public required List<AttachmentData> Attachments { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required DateTime UpdatedAt { get; set; }
    public required bool CanModify { get; set; }
    public required UserDetails Owner { get; set; }
}

public record UserDetails
{
    public required string Color { get; set; }
    public required string DisplayName { get; set; }
    public required string Initials { get; set; }
}

public record IssueAssigneeDetails : UserDetails
{
    public required bool IsCurrentUser { get; set; }
}

public record DetailIssueAttributeDto
{
    public required long Id { get; set; }
    public required AttributeType Type { get; set; }
    public required string Name { get; set; }
    public required string Value { get; set; }
    public required string Color { get; set; }
    public required IssueAttributeListValueDto[] ListValues { get; set; }
}

public record IssueAttributeListValueDto
{
    public required long Id { get; set; }
    public required string Name { get; set; }
}

public class IssueDetailDtoData
{
    public required long Id { get; set; }
    public required Guid AssigneeId { get; set; }
    public required string AssigneeDisplayName { get; set; }
    public required string AssigneeInitials { get; set; }
    public required string AssigneeColor { get; set; }
    public required DateTime Time { get; set; }
    public required DateTime UpdatedAt { get; set; }
    public required long TelegramId { get; set; }
    public required string OwnerDisplayName { get; set; }
    public required string OwnerInitials { get; set; }
    public required string OwnerColor { get; set; }
    public required string? Content { get; set; }
    public required long CategoryId { get; set; }
    public required string? CategoryName { get; set; }
    public required string? CategoryColor { get; set; }
    public required long StatusId { get; set; }
    public required string? StatusName { get; set; }
    public required string? StatusColor { get; set; }
    public required long OrganizationId { get; set; }
    public required int Number { get; set; }
    public required long SpaceId { get; set; }
    public required string SpaceKey { get; set; }
    public required string SpaceName { get; set; }
    public required string SpaceColor { get; set; }
}

public record BatchRequest
{
    public int Skip { get; set; }
    public required int Take { get; set; }
}

public class BatchResult<T>
{
    public required long Offset { get; set; }
    public required bool HasNext { get; set; }
    public required IReadOnlyCollection<T> Data { get; set; }
}

public class InitialBatchResult<T> : BatchResult<T>
{
    public required long TotalCount { get; set; }
}

public class ColumnSummary
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public required int Count { get; set; }
}

public record EpicSummary
{
    public required long Id { get; set; }
    public required string Name { get; set; }
    public required string? Color { get; set; }
    public required ColumnSummary[] Columns { get; set; }
    public required DateTime TouchedAt { get; set; }
    public required bool IsDefault { get; set; }
}

public record AttachmentData : MediaInfo
{
    public required Guid Id { get; init; }
}

public record AddCommentRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    
    [MaxLength(Constraints.MaxCommentLength)]
    public required string Text { get; set; }
    public required string IssueKey { get; set; }
    public IFormFile[] Files { get; set; } = [];
}

public record UpdateCommentRequest
{
    public OrganizationAuthData AuthData { get; set; } = new();
    public long CommentId { get; set; }
    public required string Text { get; set; }
    public Guid[] RemoveAttachmentIds { get; set; } = [];
    public IFormFile[] AddFiles { get; set; } = [];
}

public record DeleteCommentRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public long CommentId { get; set; }
}

public record ChangesIssuesOrderRequest
{
    public OrganizationAuthData AuthData { get; set; }

    /// <summary>
    /// Issue to update order key.
    /// </summary>
    public required string[] IssueKeys { get; set; } = [];
    
    /// <summary>
    /// The boards card key before or after which the issue should appear.
    /// </summary>
    public required string TargetKey { get; set; }
    
    /// <summary>
    /// Target type.
    /// </summary>
    public required OrderTargetType TargetType { get; set; }
}

public record UpdateIssuesStatusRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required string[] IssueKeys { get; set; } = [];
    public required long StatusId { get; set; }
}

public record GetIssueCommentsRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public string IssueKey { get; set; } = string.Empty;
    public required PaginationData Pagination { get; set; }
}


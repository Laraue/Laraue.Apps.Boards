using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.McpHost.Resources;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.AttributeRequests;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.McpHost.Services;

/// <summary>
/// Backs <see cref="Laraue.Apps.Boards.McpHost.Tools.IssueTools"/> - the tool type is a thin MCP
/// adapter (attributes + parameter descriptions only), this is where the actual query/permission/
/// mutation logic lives. Goes through the exact same <see cref="IAccessService"/> checks and
/// <see cref="ICoreIssuesService"/> calls the REST API uses - no new permission or mutation logic.
/// </summary>
public interface IIssueMcpService
{
    Task<IssueListPage> ListIssues(
        OrganizationAuthData authData,
        string? spaceKey,
        long? statusId,
        Guid? assigneeId,
        int? page,
        int? count,
        CancellationToken cancellationToken);

    Task<IssueDetail> GetIssue(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Moves the issue to <paramref name="statusId"/> - any status the caller can move issues to
    /// (via <see cref="IAccessService.CanMoveToStatus"/>), not necessarily one in the issue's
    /// current epic; moving to a different epic's status is the REST API's own behavior too,
    /// nothing MCP-specific. Call <see cref="ListStatuses"/> first to find a valid id.
    /// </summary>
    Task EditIssueStatus(
        OrganizationAuthData authData,
        string issueKey,
        long statusId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Creates an issue in the space <paramref name="statusId"/> belongs to - call
    /// <see cref="ListStatuses"/> first to find one; the destination space is derived entirely
    /// from it, same as the REST API's own <c>IssuesService.Create</c> (no separate space
    /// parameter to cross-validate against). <paramref name="assigneeId"/> must belong to the
    /// caller's organization (same check REST's <c>IssuesService.Create</c> runs) - omit it to
    /// assign the issue to the caller, same default <c>ICoreIssuesService.Create</c> itself falls
    /// back to when nothing is set. <paramref name="attributes"/> maps attribute id to a
    /// plain-text value (see <see cref="ListAttributes"/> for the ids/types/expected format per
    /// attribute) - omit or pass null/empty to leave every attribute unset. <paramref name="files"/>
    /// are attached in addition to any already on the issue (there are none yet, for a new issue).
    /// Returns the new issue's key.
    /// </summary>
    Task<string> CreateIssue(
        OrganizationAuthData authData,
        string content,
        long statusId,
        Guid? assigneeId,
        IReadOnlyDictionary<long, string>? attributes,
        IReadOnlyList<FileAttachment>? files,
        CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="assigneeId"/> must belong to the caller's organization (same check REST's
    /// <c>IssuesService.Update</c> runs) - omit it to leave the current assignee untouched.
    /// <paramref name="attributes"/> maps attribute id to a plain-text value, same as
    /// <see cref="CreateIssue"/> - a <c>null</c> (omitted) dictionary leaves every attribute
    /// untouched, while an empty (but non-null) one clears every attribute the issue currently
    /// has. <paramref name="files"/> are attached in addition to the issue's existing
    /// attachments. <paramref name="removeAttachmentIds"/> removes existing attachments by id
    /// (see <see cref="GetIssue"/>'s <c>Attachments</c>) - both can be given in the same call to
    /// replace one attachment with another.
    /// </summary>
    Task EditIssue(
        OrganizationAuthData authData,
        string issueKey,
        string content,
        Guid? assigneeId,
        IReadOnlyDictionary<long, string>? attributes,
        IReadOnlyList<FileAttachment>? files,
        IReadOnlyList<Guid>? removeAttachmentIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deletes an issue - same <c>CanDeleteIssue</c> permission check and
    /// <c>ICoreIssuesService.Delete</c> call REST's own <c>IssuesService.Delete</c> uses. The
    /// issue stops appearing anywhere except its own audit trail, matching the soft-delete
    /// convention documented in AGENTS.md.
    /// </summary>
    Task DeleteIssue(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the spaces available to the caller (same set the REST API's own
    /// <c>SpacesController.GetAll</c> returns) - the keys <see cref="ListIssues"/>'s
    /// <c>spaceKey</c> filter and <see cref="ListStatuses"/> take. Not paginated, same as the
    /// REST endpoint - an organization's space count is naturally small.
    /// </summary>
    Task<IReadOnlyList<SpaceSummary>> ListSpaces(
        OrganizationAuthData authData,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the statuses available in a space, grouped by epic - the ids a caller can pass to
    /// <see cref="CreateIssue"/>/<see cref="EditIssueStatus"/>.
    /// </summary>
    Task<IReadOnlyList<EpicStatusSummary>> ListStatuses(
        OrganizationAuthData authData,
        string spaceKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists every custom attribute defined for the caller's organization (attributes are
    /// org-wide, not scoped to a space/epic) - the ids/types a caller can pass to
    /// <see cref="CreateIssue"/>/<see cref="EditIssue"/>'s <c>attributes</c> map, and for
    /// list-typed attributes, the allowed values (each with its own id, to pass as the value).
    /// </summary>
    Task<IReadOnlyList<AttributeSummary>> ListAttributes(
        OrganizationAuthData authData,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists organization members visible to the caller (same set the REST API's
    /// <c>OrganizationsController.GetMembers</c> returns) - the ids <see cref="ListIssues"/>'s
    /// <c>assigneeId</c> filter takes. Not paginated, same as the REST endpoint - organization
    /// membership is naturally small. <paramref name="spaceKey"/> narrows the result to members
    /// visible in that one space (same resolution/permission check <see cref="ListStatuses"/>
    /// runs) - useful when picking an <c>assigneeId</c> for <see cref="CreateIssue"/>/
    /// <see cref="EditIssue"/> in that space, since an assignee needs to actually be able to see
    /// the issue there. Omit it to list every member visible anywhere, same as before.
    /// </summary>
    Task<IReadOnlyList<MemberSummary>> ListMembers(
        OrganizationAuthData authData,
        string? spaceKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads an issue attachment's original file content, by the id <see cref="GetIssue"/>'s
    /// <c>Attachments</c> already exposes (the same one <c>removeAttachmentIds</c> takes) - not a
    /// separate file id, so there's only ever one id per attachment for a caller to track.
    /// Permission is checked against the attachment's own issue, same as <see cref="GetIssue"/>.
    /// </summary>
    Task<FileContent> GetAttachmentContent(
        OrganizationAuthData authData,
        Guid attachmentId,
        CancellationToken cancellationToken);

    /// <summary>Returns the new comment's id.</summary>
    Task<long> CreateComment(
        OrganizationAuthData authData,
        string issueKey,
        string text,
        CancellationToken cancellationToken);

    /// <summary>
    /// Throws <see cref="NotFoundException"/> if the comment (or its issue) doesn't exist or
    /// isn't readable, <see cref="ForbiddenException"/> if it's readable but this caller isn't
    /// its author - only the comment's own owner may edit it, same rule the REST API enforces
    /// (<c>IssuesService.UpdateIssueComment</c>), not gated by <c>CanUpdateIssue</c>.
    /// </summary>
    Task EditComment(
        OrganizationAuthData authData,
        long commentId,
        string text,
        CancellationToken cancellationToken);

    /// <summary>
    /// Same existence/ownership rules as <see cref="EditComment"/> - only the comment's own
    /// owner may delete it.
    /// </summary>
    Task DeleteComment(
        OrganizationAuthData authData,
        long commentId,
        CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="CanEdit"/>/<see cref="CanDelete"/> mirror the REST API's own per-item
/// <c>CanEdit</c> exposure (<c>IssuesService</c>'s search results) - so a caller can tell whether
/// <see cref="IIssueMcpService.EditIssue"/>/<see cref="IIssueMcpService.DeleteIssue"/> will
/// actually succeed before calling them, rather than discovering it via a thrown
/// <see cref="ForbiddenException"/>.
/// </summary>
public sealed record IssueSummary(string Key, string Title, string Status, string Assignee, bool CanEdit, bool CanDelete);

/// <summary>
/// One page of <see cref="IssueSummary"/> results - same page/perPage/hasNextPage shape the REST
/// API's own paginated endpoints use (<c>ShortPaginatedResult{T}</c>), so callers page through
/// results the same way rather than being limited to a single fixed-size batch.
/// </summary>
public sealed record IssueListPage(IReadOnlyList<IssueSummary> Issues, long Page, bool HasNextPage);

/// <summary>
/// <see cref="CanManage"/> is true only for the comment's own author - the single rule
/// <see cref="IIssueMcpService.EditComment"/>/<see cref="IIssueMcpService.DeleteComment"/> both
/// enforce (not gated by <c>CanUpdateIssue</c>) - so a caller can tell upfront whether either will
/// succeed, rather than discovering it via a thrown <see cref="ForbiddenException"/>.
/// </summary>
public sealed record IssueCommentSummary(long Id, string Author, string Text, DateTime CreatedAt, bool CanManage);

/// <summary>An issue's attachment, as returned by <see cref="IIssueMcpService.GetIssue"/> - its
/// <see cref="Id"/> is what <see cref="IIssueMcpService.EditIssue"/>'s <c>removeAttachmentIds</c>
/// takes to remove it.</summary>
public sealed record IssueAttachmentSummary(Guid Id, string? FileName);

/// <summary>
/// <see cref="CanEdit"/>/<see cref="CanDelete"/> mirror the REST API's own <c>IssueDetailDto.
/// CanEdit</c> exposure - so a caller can tell whether <see cref="IIssueMcpService.EditIssue"/>/
/// <see cref="IIssueMcpService.DeleteIssue"/> will actually succeed before calling them, rather
/// than discovering it via a thrown <see cref="ForbiddenException"/>.
/// </summary>
public sealed record IssueDetail(
    string Key,
    string? Content,
    string Status,
    string Assignee,
    bool CanEdit,
    bool CanDelete,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<IssueCommentSummary> Comments,
    IReadOnlyList<IssueAttachmentSummary> Attachments);

/// <summary>A space, as returned by <see cref="IIssueMcpService.ListSpaces"/> - its
/// <see cref="Key"/> is what <see cref="IIssueMcpService.ListIssues"/>'s <c>spaceKey</c> and
/// <see cref="IIssueMcpService.ListStatuses"/> take.</summary>
/// <summary>
/// <see cref="CanCreateIssue"/> tells a caller upfront whether <see cref="IIssueMcpService.
/// CreateIssue"/> will actually succeed for a status in this space - <c>create_issue</c>'s real
/// permission check resolves the target status's epic down to its space (no separate epic-level
/// permission table), so it's the same flag regardless of which status within the space is
/// eventually chosen.
/// </summary>
public sealed record SpaceSummary(string Key, string Name, bool CanCreateIssue);

public sealed record StatusSummary(long Id, string Name);

public sealed record EpicStatusSummary(string EpicName, IReadOnlyList<StatusSummary> Statuses);

/// <summary>An organization member, as returned by <see cref="IIssueMcpService.ListMembers"/> -
/// its <see cref="Id"/> is what <see cref="IIssueMcpService.ListIssues"/>'s <c>assigneeId</c> and
/// <see cref="IIssueMcpService.CreateIssue"/>'s implicit self-assign both deal in.</summary>
public sealed record MemberSummary(Guid Id, string DisplayName);

/// <summary>
/// <see cref="Type"/> is <see cref="AttributeType"/>'s name (e.g. "Text", "Integer", "Date") -
/// the expected format for the plain-text value <see cref="IIssueMcpService.CreateIssue"/>/
/// <see cref="IIssueMcpService.EditIssue"/> take per attribute, keyed by <see cref="Id"/>.
/// <see cref="ListValues"/> is only populated for <see cref="AttributeType.List"/> - null
/// otherwise; a List value is set by passing one of its <see cref="AttributeListValueSummary.Id"/>s
/// as plain text (e.g. "42"), not its display value.
/// </summary>
public sealed record AttributeSummary(long Id, string Name, string Type, IReadOnlyList<AttributeListValueSummary>? ListValues);

public sealed record AttributeListValueSummary(long Id, string Value);

/// <summary>
/// A file to attach, base64-encoded - the only shape an MCP tool call can practically carry a
/// file in, since there's no multipart upload channel here the way the REST API's
/// <c>IFormFile[]</c> gets one. <see cref="ContentType"/> must be one of
/// <see cref="SystemMimeTypes.Supported"/> (images only, same restriction the REST API has).
/// </summary>
public sealed record FileAttachment(string FileName, string ContentType, string Base64Content);

public class IssueMcpService(
    DatabaseContext context,
    IAccessService accessService,
    ICoreIssuesService coreIssuesService,
    ICoreSpacesService coreSpacesService,
    ICoreIssueAttributesService coreIssueAttributesService,
    ICoreFilesService coreFilesService,
    IDateTimeProvider dateTimeProvider)
    : IIssueMcpService
{
    private const int MaxResults = 50;
    private const int TitleSnippetLength = 120;

    public Task<IssueListPage> ListIssues(
        OrganizationAuthData authData,
        string? spaceKey,
        long? statusId,
        Guid? assigneeId,
        int? page,
        int? count,
        CancellationToken cancellationToken)
    {
        var perPage = Math.Clamp(count ?? MaxResults, 1, MaxResults);
        var pagination = new PaginationData { Page = page ?? 0, PerPage = perPage };

        return accessService.GetAvailableIssues<IssueListPage>(authData, async issues =>
        {
            var query = issues;

            if (!string.IsNullOrWhiteSpace(spaceKey))
                query = query.Where(i => i.Status!.Epic!.Space!.Key == spaceKey);

            if (statusId is not null)
                query = query.Where(i => i.StatusId == statusId);

            if (assigneeId is not null)
                query = query.Where(i => i.AssigneeId == assigneeId);

            var result = await query
                .OrderByDescending(i => i.UpdatedAt)
                .Select(i => new
                {
                    SpaceKey = i.IssueNumber!.Space!.Key,
                    i.IssueNumber.Number,
                    i.Content,
                    Status = i.Status!.Name,
                    Assignee = i.Assignee!.DisplayName,
                })
                .ShortPaginateEFAsync(pagination, cancellationToken);

            // Permissions are space-scoped (see IAccessService), so every issue in the same space
            // shares the same CanEdit/CanDelete - one batched query per permission across just the
            // spaces present on this page, not a per-issue check, same optimization REST's own
            // IssuesService.Search uses for CanEdit.
            var pageSpaceKeys = result.Data.Select(x => x.SpaceKey).Distinct().ToArray();

            var spaceKeysWithUpdate = pageSpaceKeys.Length == 0
                ? []
                : (await accessService.GetSpacesWithAllowedIssuesUpdate(
                    authData,
                    q => q
                        .Where(s => pageSpaceKeys.Contains(s.Key))
                        .Select(s => s.Key)
                        .ToArrayAsyncEF(cancellationToken),
                    cancellationToken)).ToHashSet();

            var spaceKeysWithDelete = pageSpaceKeys.Length == 0
                ? []
                : (await accessService.GetSpacesWithAllowedIssuesDelete(
                    authData,
                    q => q
                        .Where(s => pageSpaceKeys.Contains(s.Key))
                        .Select(s => s.Key)
                        .ToArrayAsyncEF(cancellationToken),
                    cancellationToken)).ToHashSet();

            var summaries = result.Data
                .Select(x => new IssueSummary(
                    new IssueKey(x.SpaceKey, x.Number).ToString(),
                    ContentSnippet(x.Content),
                    x.Status,
                    x.Assignee,
                    spaceKeysWithUpdate.Contains(x.SpaceKey),
                    spaceKeysWithDelete.Contains(x.SpaceKey)))
                .ToList();

            return new IssueListPage(summaries, result.Page, result.HasNextPage);
        }, cancellationToken);
    }

    public async Task<IssueDetail> GetIssue(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        var accessLevels = await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "read"));

        var issue = await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => new
            {
                i.Content,
                StatusName = i.Status!.Name,
                AssigneeName = i.Assignee!.DisplayName,
                i.CreatedAt,
                i.UpdatedAt,
            })
            .SingleAsync(cancellationToken);

        var comments = await context.ActiveIssueComments()
            .Where(c => c.IssueId == issueId)
            .OrderBy(c => c.Id)
            .Select(c => new IssueCommentSummary(
                c.Id, c.Owner!.DisplayName, c.Text, c.CreatedAt, c.OwnerId == authData.UserId))
            .ToListAsyncEF(cancellationToken);

        var attachments = await context.IssueAttachments
            .Where(a => a.IssueId == issueId)
            .Select(a => new IssueAttachmentSummary(a.AttachmentId, a.Attachment!.File!.Name))
            .ToListAsyncEF(cancellationToken);

        return new IssueDetail(
            key.ToString(),
            issue.Content,
            issue.StatusName,
            issue.AssigneeName,
            accessLevels.CanUpdateIssue,
            accessLevels.CanDeleteIssue,
            issue.CreatedAt,
            issue.UpdatedAt,
            comments,
            attachments);
    }

    public async Task EditIssueStatus(
        OrganizationAuthData authData,
        string issueKey,
        long statusId,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        var canMove = await accessService.CanMoveToStatus(authData, statusId, cancellationToken);
        if (!canMove)
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "Status", statusId));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.UpdateIssuesStatus([issueId], statusId, authData.UserId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<string> CreateIssue(
        OrganizationAuthData authData,
        string content,
        long statusId,
        Guid? assigneeId,
        IReadOnlyDictionary<long, string>? attributes,
        IReadOnlyList<FileAttachment>? files,
        CancellationToken cancellationToken)
    {
        // Same shape as the REST API's own IssuesService.Create - permission is derived entirely
        // from statusId's own epic, no separate space parameter to cross-validate against.
        var validationData = await context.ActiveStatuses()
            .Where(s => s.Id == statusId)
            .Select(x => new { x.EpicId })
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.EntityNotFound, "Status", statusId), cancellationToken);

        await accessService.GetAccessLevelsByEpicId(authData, validationData.EpicId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Status", statusId))
            .EnsureOrThrowNotFound(a => a.CanCreateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Status", statusId, "issue creation"));

        if (assigneeId is not null)
            await EnsureUserBelongsToOrganization(authData.OrganizationId, assigneeId.Value, cancellationToken);

        var attributeRequests = await ResolveAttributeRequests(authData.OrganizationId, attributes, cancellationToken);
        var uploadedFiles = await UploadFiles(files, cancellationToken);

        var issueCreate = new IssueCreateRequest(statusId, dateTimeProvider.UtcNow)
            .SetContent(content)
            .LinkNewAttachments(uploadedFiles);
        if (assigneeId is not null)
            issueCreate = issueCreate.SetAssignee(assigneeId.Value);
        if (attributes is not null)
            issueCreate = issueCreate.SetAttributes(attributeRequests);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var issueId = await coreIssuesService.Create(authData.ToActor(), issueCreate, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await context.ActiveIssues()
            .Where(i => i.Id == issueId)
            .Select(i => new IssueKey(i.IssueNumber!.Space!.Key, i.IssueNumber.Number).ToString())
            .FirstAsyncEF(cancellationToken);
    }

    public async Task EditIssue(
        OrganizationAuthData authData,
        string issueKey,
        string content,
        Guid? assigneeId,
        IReadOnlyDictionary<long, string>? attributes,
        IReadOnlyList<FileAttachment>? files,
        IReadOnlyList<Guid>? removeAttachmentIds,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        if (assigneeId is not null)
            await EnsureUserBelongsToOrganization(authData.OrganizationId, assigneeId.Value, cancellationToken);

        var attributeRequests = await ResolveAttributeRequests(authData.OrganizationId, attributes, cancellationToken);
        var uploadedFiles = await UploadFiles(files, cancellationToken);

        var issueUpdate = new IssueUpdateRequest()
            .SetContent(content)
            .LinkNewAttachments(uploadedFiles)
            .UnlinkAttachments(removeAttachmentIds ?? []);
        if (assigneeId is not null)
            issueUpdate = issueUpdate.SetAssignee(assigneeId.Value);
        if (attributes is not null)
            issueUpdate = issueUpdate.SetAttributes(attributeRequests);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.Update(issueId, authData.ToActor(), issueUpdate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteIssue(
        OrganizationAuthData authData,
        string issueKey,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFound, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanDeleteIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "delete"));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.Delete(issueId, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Same check REST's <c>IssuesService.EnsureUserBelongsToOrganization</c> runs before
    /// accepting an <c>AssigneeId</c> - an MCP caller has no UI stopping them from typing an
    /// arbitrary Guid, so this has to be enforced here too rather than trusted.
    /// </summary>
    private async Task EnsureUserBelongsToOrganization(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var userExists = await accessService.GetOrganizationMembers(
            organizationId,
            members => members.Where(x => x.UserId == userId).AnyAsyncEF(cancellationToken));

        if (!userExists)
            throw new NotFoundException(string.Format(ErrorMessages.UserNotBelongsToOrganization, userId));
    }

    public async Task<IReadOnlyList<SpaceSummary>> ListSpaces(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        var spaces = await accessService.GetAvailableSpaces(
            authData,
            items => items
                .Select(x => new { x.Key, x.Name })
                .ToListAsyncEF(cancellationToken),
            includeDeleted: false,
            cancellationToken);

        var spaceKeysWithCreate = (await accessService.GetSpacesWithAllowedIssueCreation(
            authData,
            q => q.Select(s => s.Key).ToArrayAsyncEF(cancellationToken),
            cancellationToken)).ToHashSet();

        return spaces
            .Select(x => new SpaceSummary(x.Key, x.Name, spaceKeysWithCreate.Contains(x.Key)))
            .ToList();
    }

    public async Task<IReadOnlyList<EpicStatusSummary>> ListStatuses(
        OrganizationAuthData authData,
        string spaceKey,
        CancellationToken cancellationToken)
    {
        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(authData.OrganizationId, spaceKey, cancellationToken);

        await accessService.GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", spaceKey))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Space", spaceKey, "read"));

        return await context.ActiveEpics()
            .Where(e => e.SpaceId == spaceId)
            .OrderBy(e => e.Id)
            .Select(e => new EpicStatusSummary(
                e.Name,
                e.Statuses!
                    .Where(s => s.DeletedAt == null)
                    .OrderBy(s => s.SortOrder)
                    .Select(s => new StatusSummary(s.Id, s.Name))
                    .ToList()))
            .ToListAsyncEF(cancellationToken);
    }

    public async Task<IReadOnlyList<AttributeSummary>> ListAttributes(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return await context.Attributes
            .Where(a => a.OrganizationId == authData.OrganizationId)
            .OrderBy(a => a.Id)
            .Select(a => new AttributeSummary(
                a.Id,
                a.Name,
                a.AttributeType.ToString(),
                a.AttributeType == AttributeType.List
                    ? a.AttributeListValues!.Select(v => new AttributeListValueSummary(v.Id, v.Value)).ToList()
                    : null))
            .ToListAsyncEF(cancellationToken);
    }

    public async Task<IReadOnlyList<MemberSummary>> ListMembers(
        OrganizationAuthData authData,
        string? spaceKey,
        CancellationToken cancellationToken)
    {
        long[] spaceIds;
        if (spaceKey is not null)
        {
            var spaceId = await coreSpacesService
                .GetSpaceIdBySpaceKey(authData.OrganizationId, spaceKey, cancellationToken);

            await accessService.GetAccessLevelsBySpaceId(authData, spaceId, includeDeleted: false, cancellationToken)
                .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Space", spaceKey))
                .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Space", spaceKey, "read"));

            spaceIds = [spaceId];
        }
        else
        {
            spaceIds = await accessService.GetAvailableSpaces(
                authData,
                query => query
                    .Select(s => s.Id)
                    .ToArrayAsyncEF(cancellationToken),
                includeDeleted: false,
                cancellationToken);
        }

        return await accessService.GetVisibleUsers(
            spaceIds,
            query => query
                .Select(x => new MemberSummary(x.UserId, x.User!.DisplayName))
                .ToListAsyncEF(cancellationToken));
    }

    private async Task<IReadOnlyList<SetIssueAttributeRequest>> ResolveAttributeRequests(
        long organizationId,
        IReadOnlyDictionary<long, string>? attributes,
        CancellationToken cancellationToken)
    {
        if (attributes is null || attributes.Count == 0)
            return [];

        var ids = attributes.Keys.ToArray();

        var attributeTypeById = await context.Attributes
            .Where(a => a.OrganizationId == organizationId && ids.Contains(a.Id))
            .ToDictionaryAsyncEF(a => a.Id, a => a.AttributeType, cancellationToken);

        var errors = new List<string?>();
        var attributeValues = new List<AttributeValue>();

        foreach (var (attributeId, rawValue) in attributes)
        {
            if (!attributeTypeById.TryGetValue(attributeId, out var attributeType))
            {
                errors.Add(string.Format(ErrorMessages.EntityNotFound, "Attribute", attributeId));
                continue;
            }

            var (value, error) = ParseAttributeValue(attributeId, attributeType, rawValue);
            if (error is not null)
                errors.Add(error);
            else
                attributeValues.Add(value!);
        }

        // Still run the shared batched validation (list value existence, etc.) on whatever
        // parsed successfully, rather than bailing out here on the first parse error - otherwise
        // a bad value on one attribute would hide a genuine core-side error on another, the same
        // fail-fast problem already fixed on the REST side. Both layers' errors are merged into
        // one exception below.
        IReadOnlyList<SetIssueAttributeRequest> requests = [];
        if (attributeValues.Count > 0)
        {
            try
            {
                requests = await coreIssueAttributesService.BuildSetRequests(
                    organizationId, attributeValues.ToArray(), cancellationToken);
            }
            catch (BadRequestException ex)
            {
                errors.AddRange(ex.Errors["attributeValues"]);
            }
        }

        if (errors.Count > 0)
            throw new BadRequestException(new Dictionary<string, string?[]>
            {
                [nameof(attributes)] = errors.ToArray(),
            });

        return requests;
    }

    /// <summary>
    /// Parses an MCP caller's plain-text value into the correctly-typed <see cref="AttributeValue"/>
    /// for <paramref name="attributeType"/> - the one piece of this flow that can't be shared with
    /// the REST API, which never parses text (its client already sends an already-typed value).
    /// For <see cref="AttributeType.List"/>, the caller passes one of list_attributes' list value
    /// ids as plain text (e.g. "42"), not the option's display text.
    /// </summary>
    private static (AttributeValue? Value, string? Error) ParseAttributeValue(
        long attributeId,
        AttributeType attributeType,
        string rawValue)
    {
        switch (attributeType)
        {
            case AttributeType.Text:
                return (new StringAttributeValue { AttributeId = attributeId, Value = rawValue }, null);

            case AttributeType.Integer:
                if (!long.TryParse(rawValue, out var integerValue))
                    return (null, string.Format(ErrorMessages.AttributeValueInvalid, attributeId, "integer"));
                return (new IntegerAttributeValue { AttributeId = attributeId, Value = integerValue }, null);

            case AttributeType.Decimal:
                if (!decimal.TryParse(rawValue, out var decimalValue))
                    return (null, string.Format(ErrorMessages.AttributeValueInvalid, attributeId, "decimal"));
                return (new DecimalAttributeValue { AttributeId = attributeId, Value = decimalValue }, null);

            case AttributeType.Date:
                if (!DateOnly.TryParse(rawValue, out var dateValue))
                    return (null, string.Format(ErrorMessages.AttributeValueInvalid, attributeId, "date (e.g. 2026-01-01)"));
                return (new DateAttributeValue { AttributeId = attributeId, Value = dateValue }, null);

            case AttributeType.DateTime:
                if (!DateTime.TryParse(rawValue, out var dateTimeValue))
                    return (null, string.Format(ErrorMessages.AttributeValueInvalid, attributeId, "date-time (e.g. 2026-01-01 12:00)"));
                return (new DateTimeAttributeValue { AttributeId = attributeId, Value = dateTimeValue }, null);

            case AttributeType.List:
                if (!long.TryParse(rawValue, out var listValueId))
                    return (null, string.Format(ErrorMessages.AttributeValueInvalid, attributeId, "list value id, from list_attributes"));
                return (new EnumAttributeValue { AttributeId = attributeId, ValueId = listValueId }, null);

            default:
                throw new ArgumentOutOfRangeException(nameof(attributeType), attributeType, null);
        }
    }

    /// <summary>
    /// Decodes and uploads every <paramref name="files"/> entry via the same
    /// <see cref="ICoreFilesService"/> the REST API's own <c>IssuesService.UploadFiles</c> uses -
    /// each attachment ends up stored identically regardless of which host created it. Collects
    /// every problem across every file (unsupported type, bad base64, too large) into one
    /// <see cref="BadRequestException"/> rather than failing on the first bad file.
    /// </summary>
    private async Task<IReadOnlyList<MediaInfo>> UploadFiles(
        IReadOnlyList<FileAttachment>? files,
        CancellationToken cancellationToken)
    {
        if (files is null || files.Count == 0)
            return [];

        var errors = new List<string?>();
        var decoded = new List<(FileAttachment File, byte[] Bytes)>();

        foreach (var file in files)
        {
            if (!SystemMimeTypes.Supported.Contains(file.ContentType))
            {
                errors.Add(string.Format(
                    ErrorMessages.FileUnsupportedMimeType,
                    file.FileName,
                    file.ContentType,
                    string.Join(", ", SystemMimeTypes.Supported)));
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(file.Base64Content);
            }
            catch (FormatException)
            {
                errors.Add(string.Format(ErrorMessages.FileInvalidBase64, file.FileName));
                continue;
            }

            if (bytes.Length > SystemMimeTypes.MaxFileSizeBytes)
            {
                errors.Add(string.Format(ErrorMessages.FileTooLarge, file.FileName));
                continue;
            }

            decoded.Add((file, bytes));
        }

        if (errors.Count > 0)
            throw new BadRequestException(new Dictionary<string, string?[]>
            {
                [nameof(files)] = errors.ToArray(),
            });

        var uploaded = new List<MediaInfo>();
        foreach (var (file, bytes) in decoded)
        {
            using var stream = new MemoryStream(bytes);
            uploaded.Add(await coreFilesService.UploadFile(file.FileName, file.ContentType, stream, cancellationToken));
        }

        return uploaded;
    }

    public async Task<FileContent> GetAttachmentContent(
        OrganizationAuthData authData,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var attachmentData = await context.IssueAttachments
            .Where(x => x.AttachmentId == attachmentId)
            .Select(x => new { x.IssueId, FileId = x.Attachment!.FileId, FileSize = x.Attachment.File!.Size })
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.EntityNotFound, "Attachment", attachmentId), cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, attachmentData.IssueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", attachmentData.IssueId))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Issue", attachmentData.IssueId, "read"));

        // Uploads are already capped at SystemMimeTypes.MaxFileSizeBytes (see UploadFiles below),
        // but this guards against a legacy/otherwise-larger file predating that cap - reject it
        // before ever opening a stream over it, rather than relying solely on GetAttachment's own
        // runtime cap on the copy itself.
        if (attachmentData.FileSize > SystemMimeTypes.MaxFileSizeBytes)
            throw new BadRequestException(nameof(attachmentId), string.Format(ErrorMessages.AttachmentTooLargeToDownload, attachmentId));

        return await coreFilesService.GetFileContent(attachmentData.FileId, cancellationToken);
    }

    public async Task<long> CreateComment(
        OrganizationAuthData authData,
        string issueKey,
        string text,
        CancellationToken cancellationToken)
    {
        var key = new IssueKey(issueKey);

        var issueId = await GetIssueIdByIssueKey(authData.OrganizationId, key, cancellationToken);

        await accessService.GetAccessLevelsByIssueId(authData, issueId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Issue", key))
            .EnsureOrThrowForbidden(a => a.CanUpdateIssue, string.Format(ErrorMessages.EntityActionForbidden, "Issue", key, "update"));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var commentId = await coreIssuesService.AddComment(issueId, authData.ToActor(), text, [], cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return commentId;
    }

    public async Task EditComment(
        OrganizationAuthData authData,
        long commentId,
        string text,
        CancellationToken cancellationToken)
    {
        // Two distinct failure modes: the comment/its issue doesn't exist or isn't readable
        // (404), vs. it's readable but this caller isn't its author (403) - same rule the REST
        // API's UpdateIssueComment enforces for the second case (owner-only, not CanUpdateIssue).
        await accessService.GetAccessLevelsByCommentId(authData, commentId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Comment", commentId))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Comment", commentId, "read"));

        var comment = await context.ActiveIssueComments()
            .Where(x => x.Id == commentId)
            .Select(x => new { x.Id, x.OwnerId })
            .SingleAsync(cancellationToken);

        if (comment.OwnerId != authData.UserId)
            throw new ForbiddenException(string.Format(ErrorMessages.EntityActionForbidden, "Comment", commentId, "edit"));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.UpdateComment(comment.Id, new Actor(comment.OwnerId, authData.ApiKeyId), text, [], [], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteComment(
        OrganizationAuthData authData,
        long commentId,
        CancellationToken cancellationToken)
    {
        await accessService.GetAccessLevelsByCommentId(authData, commentId, includeDeleted: false, cancellationToken)
            .OrThrowNotFound(string.Format(ErrorMessages.EntityNotFoundOrNotAccessible, "Comment", commentId))
            .EnsureOrThrowForbidden(a => a.CanRead, string.Format(ErrorMessages.EntityActionForbidden, "Comment", commentId, "read"));

        var comment = await context.ActiveIssueComments()
            .Where(x => x.Id == commentId)
            .Select(x => new { x.Id, x.OwnerId })
            .SingleAsync(cancellationToken);

        if (comment.OwnerId != authData.UserId)
            throw new ForbiddenException(string.Format(ErrorMessages.EntityActionForbidden, "Comment", commentId, "delete"));

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await coreIssuesService.DeleteComment(comment.Id, authData.ToActor(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task<long> GetIssueIdByIssueKey(long organizationId, IssueKey issueKey, CancellationToken cancellationToken)
    {
        return context.IssueNumbers
            .Where(x => x.Number == issueKey.Number)
            .Where(x => x.Space!.Key == issueKey.SpaceKey)
            .Where(x => x.Space!.OrganizationId == organizationId)
            .Where(x => x.Issue!.DeletedAt == null)
            .Select(x => x.IssueId)
            .FirstOrThrowNotFoundEFAsync(string.Format(ErrorMessages.IssueNotFoundInOrganization, issueKey), cancellationToken);
    }

    private static string ContentSnippet(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var firstLine = content.Split('\n', 2)[0];

        return TextTruncation.Truncate(firstLine, TitleSnippetLength);
    }
}

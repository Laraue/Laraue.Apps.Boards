using System.Linq.Expressions;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface IAccessService
{
    /// <summary>
    /// Returns organizations the user is a member of.
    /// </summary>
    Task<T> GetOrganizations<T>(
        Guid userId,
        Func<IQueryable<OrganizationUser>, Task<T>> map);

    /// <summary>
    /// Returns members of the requested organization.
    /// </summary>
    Task<T> GetOrganizationMembers<T>(
        long organizationId,
        Func<IQueryable<OrganizationUser>, Task<T>> map);

    /// <summary>
    /// Returns whether the user has the requested organization-level admin access.
    /// </summary>
    Task<bool> HasAccess(
        OrganizationAuthData authData,
        AdminAccessLevel accessLevel,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns whether the user can create spaces in the requested organization.
    /// </summary>
    Task<bool> CanCreateSpaces(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns spaces available to read for the user.
    /// </summary>
    Task<T> GetAvailableSpaces<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Space>, Task<T>> map,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Returns members that can read the requested space.
    /// </summary>
    Task<T> GetSpaceMembers<T>(
        OrganizationAuthData authData,
        long spaceId,
        Func<IQueryable<OrganizationUser>, Task<T>> map);
    
    /// <summary>
    /// Get all spaces where user can create epics.
    /// </summary>
    Task<T> GetSpacesWithAllowedEpicCreation<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Space>, Task<T>> map,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Get all spaces where user can edit issues.
    /// </summary>
    Task<T> GetSpacesWithAllowedIssuesUpdate<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Space>, Task<T>> map,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Return access level of the requested space.
    /// </summary>
    Task<AccessLevels?> GetAccessLevelsBySpaceId(
        OrganizationAuthData authData,
        long spaceId,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Returns true when user can create epics in space.
    /// </summary>
    Task<bool> CanCreateEpics(
        OrganizationAuthData authData,
        long spaceId,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Returns epics available to read for the user.
    /// </summary>
    Task<T> GetAvailableEpics<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Epic>, Task<T>> map,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Returns issues access level in the specified epic.
    /// </summary>
    Task<AccessLevels?> GetAccessLevelsByEpicId(
        OrganizationAuthData authData,
        long epicId,
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Returns issues available to read for the user.
    /// </summary>
    Task<T> GetAvailableIssues<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Issue>, Task<T>> map,
        CancellationToken cancellationToken);
    
    Task<AccessLevels?> GetAccessLevelsByIssueId(
        OrganizationAuthData authData,
        long issueId,
        CancellationToken cancellationToken);
    
    Task<bool> CanMoveToStatus(
        OrganizationAuthData authData,
        long statusId,
        CancellationToken cancellationToken);
    
    Task<bool> CanModifyStatus(
        OrganizationAuthData authData,
        long statusId,
        CancellationToken cancellationToken);
}

public class AccessService(DatabaseContext context) : IAccessService
{
    public Task<T> GetOrganizations<T>(Guid userId, Func<IQueryable<OrganizationUser>, Task<T>> map)
    {
        var query = context.OrganizationUsers
            .Where(x => x.UserId == userId);

        return map(query);
    }

    public Task<T> GetOrganizationMembers<T>(long organizationId, Func<IQueryable<OrganizationUser>, Task<T>> map)
    {
        var query = context.OrganizationUsers
            .Where(x => x.OrganizationId == organizationId);

        return map(query);
    }

    public Task<bool> HasAccess(OrganizationAuthData authData, AdminAccessLevel accessLevel, CancellationToken cancellationToken)
    {
        return GetOrganizations(authData.UserId, organizationUsers =>
        {
            return organizationUsers
                .Where(ou => ou.OrganizationId == authData.OrganizationId)
                .AnyAsyncEF(ou => ou.AdminAccessLevel.HasFlag(accessLevel), cancellationToken);
        });
    }

    public Task<bool> CanCreateSpaces(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return GetOrganizations(userId, organizationUsers =>
        {
            return organizationUsers
                .Where(ou => ou.OrganizationId == organizationId)
                .AnyAsyncEF(ou => ou.CanCreateSpaces, cancellationToken);
        });
    }

    public async Task<T> GetAvailableSpaces<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Space>, Task<T>> map,
        CancellationToken cancellationToken)
    {
        var canGloballyRead = await GetUserData(authData, x => x.CanRead, cancellationToken);
        if (canGloballyRead)
            return await map(GetAllSpacesQuery(authData));
        
        var query = context.DirectSpacePermissions
            .Where(sos => sos.OrganizationUser!.OrganizationId == authData.OrganizationId)
            .Where(sos => sos.OrganizationUser!.UserId == authData.UserId)
            .Where(sos => sos.CanRead)
            .Select(sos => sos.Space!);
        
        return await map(query); 
    }

    public Task<T> GetSpaceMembers<T>(
        OrganizationAuthData authData,
        long spaceId,
        Func<IQueryable<OrganizationUser>, Task<T>> map)
    {
        var organizationUsersWithOrganizationLevelRead = context.OrganizationUsers
            .Where(ou => ou.OrganizationId == authData.OrganizationId)
            .Where(ou => ou.CanRead)
            .Select(ou => ou.Id);

        var organizationUsersWithDirectSpaceRead = context.DirectSpacePermissions
            .Where(sos => sos.OrganizationUser!.OrganizationId == authData.OrganizationId)
            .Where(sos => sos.SpaceId == spaceId)
            .Where(sos => sos.CanRead)
            .Select(sos => sos.OrganizationUserId);

        var spaceMemberIds = organizationUsersWithOrganizationLevelRead
            .Union(organizationUsersWithDirectSpaceRead);

        var query = context.OrganizationUsers
            .Where(ou => spaceMemberIds.Contains(ou.Id));
        
        return map(query);
    }

    public Task<T> GetSpacesWithAllowedEpicCreation<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Space>, Task<T>> map,
        CancellationToken cancellationToken)
    {
        return GetSpacesWithPermissionCondition(
            authData,
            x => x.CanCreateEpics,
            x => x.CanCreateEpics,
            map,
            cancellationToken);
    }

    public Task<T> GetSpacesWithAllowedIssuesUpdate<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Space>, Task<T>> map,
        CancellationToken cancellationToken)
    {
        return GetSpacesWithPermissionCondition(
            authData,
            x => x.CanUpdateIssues,
            x => x.CanUpdateIssues,
            map,
            cancellationToken);
    }

    private async Task<T> GetSpacesWithPermissionCondition<T>(
        OrganizationAuthData authData,
        Expression<Func<OrganizationUser, bool>> whenAllowedOnOrganizationLevel,
        Expression<Func<DirectSpacePermission, bool>> whenAllowedOnSpaceLevel,
        Func<IQueryable<Space>, Task<T>> map,
        CancellationToken cancellationToken)
    {
        var canGloballyCreateEpics = await GetUserData(authData, whenAllowedOnOrganizationLevel, cancellationToken);
        if (canGloballyCreateEpics)
            return await map(GetAllSpacesQuery(authData));
        
        var query = context.DirectSpacePermissions
            .Where(sos => sos.OrganizationUser!.OrganizationId == authData.OrganizationId)
            .Where(sos => sos.OrganizationUser!.UserId == authData.UserId)
            .Where(whenAllowedOnSpaceLevel)
            .Select(sos => sos.Space!);
        
        return await map(query); 
    }

    public async Task<AccessLevels?> GetAccessLevelsBySpaceId(
        OrganizationAuthData authData,
        long spaceId,
        CancellationToken cancellationToken)
    {
        var spaceData = await GetAvailableSpaces(
            authData,
            q => q
                .Where(s => s.Id == spaceId)
                .Select(s => new { s.IsDefault })
                .FirstOrDefaultAsyncEF(cancellationToken),
            cancellationToken);
        
        if (spaceData == null)
            return null;
        
        var globalAccessLevels = await GetUserGlobalAccessLevels(authData, cancellationToken);
        var spaceAccessLevels = await GetUserSpaceAccessLevels(authData, spaceId, cancellationToken);

        var result = globalAccessLevels.Merge(spaceAccessLevels);
        if (spaceData.IsDefault)
            result.CanDeleteSpace = false;
        
        return result;
    }

    public Task<bool> CanCreateEpics(
        OrganizationAuthData authData,
        long spaceId,
        CancellationToken cancellationToken)
    {
        return GetSpacesWithAllowedEpicCreation(
            authData,
            x => x
                .Where(s => s.Id == spaceId)
                .AnyAsyncEF(cancellationToken),
            cancellationToken);
    }

    public Task<T> GetAvailableEpics<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Epic>, Task<T>> map,
        CancellationToken cancellationToken)
    {
        return GetAvailableSpaces(
            authData,
            query =>
            {
                var epicsQuery = query.SelectMany(x => x.Epics!);
                return map(epicsQuery);
            },
            cancellationToken);
    }

    public async Task<AccessLevels?> GetAccessLevelsByEpicId(
        OrganizationAuthData authData,
        long epicId,
        CancellationToken cancellationToken)
    {
        var epicData = await GetEpicData(epicId, cancellationToken);

        var accessLevels = await GetAccessLevelsBySpaceId(authData, epicData.SpaceId, cancellationToken);

        if (accessLevels is not null && epicData.IsDefault)
            accessLevels.CanDeleteEpic = false;
        
        return accessLevels;
    }

    private async Task<EpicData> GetEpicData(long epicId, CancellationToken cancellationToken)
    {
        var epic = await context.Epics
            .Where(e => e.Id == epicId)
            .Select(x => new EpicData(x.SpaceId, x.IsDefault))
            .FirstOrThrowNotFoundEFAsync("Epic is not found", cancellationToken);

        return epic;
    }

    private record EpicData(long SpaceId, bool IsDefault);

    public Task<T> GetAvailableIssues<T>(
        OrganizationAuthData authData,
        Func<IQueryable<Issue>, Task<T>> map,
        CancellationToken cancellationToken)
    {
        return GetAvailableEpics(
            authData,
            epics => map(epics
                .SelectMany(e => e.Statuses!
                    .SelectMany(i => i.Issues!))),
            cancellationToken);
    }

    public async Task<AccessLevels?> GetAccessLevelsByIssueId(
        OrganizationAuthData authData,
        long issueId,
        CancellationToken cancellationToken)
    {
        var epicData = await context.Issues
            .Where(i => i.Id == issueId)
            .Select(x => new { x.Status!.Epic!.SpaceId })
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (epicData == null)
            return null;

        return await GetAccessLevelsBySpaceId(
            authData,
            epicData.SpaceId,
            cancellationToken);
    }

    public async Task<bool> CanMoveToStatus(OrganizationAuthData authData, long statusId, CancellationToken cancellationToken)
    {
        var epicId = await GetEpicId(statusId, cancellationToken);
        if (epicId is null)
            return false;

        var accessLevels = await GetAccessLevelsByEpicId(authData, epicId.Value, cancellationToken);
        return accessLevels?.CanCreateIssue ?? false;
    }

    public async Task<bool> CanModifyStatus(OrganizationAuthData authData, long statusId, CancellationToken cancellationToken)
    {
        var epicId = await GetEpicId(statusId, cancellationToken);
        if (epicId is null)
            return false;

        var accessLevels = await GetAccessLevelsByEpicId(authData, epicId.Value, cancellationToken);
        return accessLevels?.CanUpdateEpic ?? false;
    }
    
    private async Task<long?> GetEpicId(long statusId, CancellationToken cancellationToken)
    {
        var result = await context.Statuses
            .Where(s => s.Id == statusId)
            .Select(s => new { s.EpicId })
            .FirstOrDefaultAsyncEF(cancellationToken);

        return result?.EpicId;
    }

    private Task<T> GetUserData<T>(
        OrganizationAuthData authData,
        Expression<Func<OrganizationUser, T>> map,
        CancellationToken cancellationToken)
    {
        return context.OrganizationUsers
            .Where(o => o.OrganizationId == authData.OrganizationId)
            .Where(o => o.UserId == authData.UserId)
            .Select(map)
            .FirstAsyncEF(cancellationToken);
    }
    
    private Task<AccessLevels> GetUserGlobalAccessLevels(
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return GetUserData(
            authData,
            user => new AccessLevels
            {
                CanRead = user.CanRead,
                CanCreateSpace = user.CanCreateSpaces,
                CanUpdateSpace = user.CanUpdateSpaces,
                CanDeleteSpace = user.CanDeleteSpaces,
                CanCreateEpic = user.CanCreateEpics,
                CanUpdateEpic = user.CanUpdateEpics,
                CanDeleteEpic = user.CanDeleteEpics,
                CanCreateIssue = user.CanCreateIssues,
                CanUpdateIssue = user.CanUpdateIssues,
                CanDeleteIssue =  user.CanDeleteIssues,
            },
            cancellationToken);
    }
    
    private Task<AccessLevels?> GetUserSpaceAccessLevels(
        OrganizationAuthData authData,
        long spaceId,
        CancellationToken cancellationToken)
    {
        return context.DirectSpacePermissions
            .Where(sos => sos.OrganizationUser!.OrganizationId == authData.OrganizationId)
            .Where(sos => sos.OrganizationUser!.UserId == authData.UserId)
            .Where(sos => sos.SpaceId == spaceId)
            .Select(sos => new AccessLevels
            {
                CanRead = sos.CanRead,
                CanCreateSpace = false,
                CanUpdateSpace = sos.CanUpdate,
                CanDeleteSpace = sos.CanDelete,
                CanCreateEpic = sos.CanCreateEpics,
                CanUpdateEpic = sos.CanUpdateEpics,
                CanDeleteEpic = sos.CanDeleteEpics,
                CanCreateIssue = sos.CanCreateIssues,
                CanUpdateIssue = sos.CanUpdateIssues,
                CanDeleteIssue =  sos.CanDeleteIssues,
            })
            .FirstOrDefaultAsyncEF(cancellationToken);
    }
    
    private IQueryable<Space> GetAllSpacesQuery(OrganizationAuthData authData)
    {
        return context.Spaces
            .Where(s => s.OrganizationId == authData.OrganizationId);
    }
}

public record AccessLevels
{
    public bool CanRead { get; set; }
    public bool CanCreateSpace { get; set; }
    public bool CanUpdateSpace { get; set; }
    public bool CanDeleteSpace { get; set; }
    public bool CanCreateEpic { get; set; }
    public bool CanUpdateEpic { get; set; }
    public bool CanDeleteEpic { get; set; }
    public bool CanCreateIssue { get; set; }
    public bool CanUpdateIssue { get; set; }
    public bool CanDeleteIssue { get; set; }

    public AccessLevels Merge(AccessLevels? other)
    {
        if (other is null)
            return this;
        
        return new AccessLevels
        {
            CanRead = CanRead | other.CanRead,
            CanCreateSpace = CanCreateSpace | other.CanCreateSpace,
            CanUpdateSpace = CanUpdateSpace | other.CanUpdateSpace,
            CanDeleteSpace = CanDeleteSpace | other.CanDeleteSpace,
            CanCreateEpic = CanCreateEpic | other.CanCreateEpic,
            CanUpdateEpic = CanUpdateEpic | other.CanUpdateEpic,
            CanDeleteEpic = CanDeleteEpic | other.CanDeleteEpic,
            CanCreateIssue = CanCreateIssue | other.CanCreateIssue,
            CanUpdateIssue = CanUpdateIssue | other.CanUpdateIssue,
            CanDeleteIssue = CanDeleteIssue | other.CanDeleteIssue,
        };
    }
}
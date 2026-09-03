using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Attribute = Laraue.Apps.Boards.DataAccess.Models.Attribute;

namespace Laraue.Apps.Boards.Services;

public interface ICoreOrganizationsService
{
    Task<CreateOrganizationResponse> Create(
        Guid ownerId,
        string slug,
        string name,
        string color,
        CancellationToken cancellationToken);
    
    Task Update(
        long id,
        Action<UpdateSettersBuilder<Organization>> setters,
        CancellationToken cancellationToken);
    
    Task Delete(
        long id,
        CancellationToken cancellationToken);
    
    Task<bool> HasMember(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);
    
    Task<long> AddMember(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);
    
    Task<long?> GetOrganizationIdByJoinCode(
        string code,
        CancellationToken cancellationToken);
    
    Task SetUserPermissions(
        long organizationUserId,
        UserPermissions userPermissions,
        CancellationToken cancellationToken);

    Task<UserPermissions> GetUserPermissions(
        long organizationUserId,
        CancellationToken cancellationToken);

    Task<PermittableSpace[]> GetPermittableEntities(
        long organizationId,
        CancellationToken cancellationToken);
    
    Task UpdatePreferences(
        long organizationId,
        Guid userId,
        Action<UpdateSettersBuilder<UserOrganizationPreferences>> updateSetters,
        CancellationToken cancellationToken);
    
    Task<UserOrganizationPreferencesResponse> GetPreferences(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);
    
    Task<long> CreateAttribute(
        long organizationId,
        string name,
        string color,
        AttributeType attributeType,
        string[]? listValues,
        CancellationToken cancellationToken);
    
    Task UpdateAttribute(
        long attributeId,
        string name,
        string color,
        UpdateAttributeListValueRequest[]? listValues,
        CancellationToken cancellationToken);
    
    Task DeleteAttribute(
        long attributeId,
        CancellationToken cancellationToken);
}

public class CoreOrganizationsService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider)
    : ICoreOrganizationsService
{
    public async Task<CreateOrganizationResponse> Create(
        Guid ownerId,
        string slug,
        string name,
        string color,
        CancellationToken cancellationToken)
    {
        var timestamp = dateTimeProvider.UtcNow;

        var entity = OrganizationDefaults.GetNewOrganizationEntity(
            ownerId,
            slug,
            name,
            color,
            timestamp,
            isPersonal: false);

        context.Organizations.Add(entity);
        await context.SaveChangesAsync(cancellationToken);

        return new CreateOrganizationResponse
        {
            Slug = entity.Slug,
            SlugPostfix = entity.SlugPostfix,
            Id = entity.Id,
        };
    }

    public Task Update(long id, Action<UpdateSettersBuilder<Organization>> setters, CancellationToken cancellationToken)
    {
        var date = dateTimeProvider.UtcNow;

        return context.Organizations
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(
                update =>
                {
                    setters(update);
                    update
                        .SetProperty(p => p.UpdatedAt, date);
                },
                cancellationToken);
    }

    public async Task Delete(long id, CancellationToken cancellationToken)
    {
        var isPersonalOrganization = await context.Organizations
            .Where(o => o.Type == OrganizationType.Personal)
            .Where(o => o.Id == id)
            .AnyAsyncEF(cancellationToken);

        if (isPersonalOrganization)
            throw new ForbiddenException("Personal organization cannot be deleted.");
        
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var spaceIds = context.Spaces
            .Where(c => c.OrganizationId == id)
            .Select(x => (long?)x.Id);

        await context.Epics
            .Where(x => spaceIds.Contains(x.SpaceId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Spaces
            .Where(c => c.OrganizationId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Organizations
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<bool> HasMember(long organizationId, Guid userId, CancellationToken cancellationToken)
    {
        return context.OrganizationUsers
            .Where(x => x.OrganizationId == organizationId)
            .Where(x => x.UserId == userId)
            .AnyAsyncEF(cancellationToken);
    }

    public async Task<long> AddMember(long organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var user = new OrganizationUser
        {
            OrganizationId = organizationId,
            UserId = userId,
        };
        
        context.OrganizationUsers.Add(user);

        await context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }

    public async Task<long?> GetOrganizationIdByJoinCode(string code, CancellationToken cancellationToken)
    {
        return (await context.Organizations
            .Where(x => x.JoinCode == code)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsyncEF(cancellationToken))?.Id;
    }
    
    public async Task SetUserPermissions(
        long organizationUserId,
        UserPermissions userPermissions,
        CancellationToken cancellationToken)
    {
        NormalizePermissions(userPermissions);
        
        // Remove old permissions
        await context.DirectSpacePermissions
            .Where(x => x.OrganizationUserId == organizationUserId)
            .ExecuteDeleteAsync(cancellationToken);
        
        // Set organization permissions
        await context.OrganizationUsers
            .Where(x => x.Id == organizationUserId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(p => p.AdminAccessLevel, userPermissions.Admin)
                
                .SetProperty(p => p.CanRead, userPermissions.Global.CanRead)
                .SetProperty(p => p.CanCreateRetros, userPermissions.Global.CanCreateRetros)

                .SetProperty(p => p.CanCreateSpaces, userPermissions.Global.CanCreateSpaces)
                .SetProperty(p => p.CanUpdateSpaces, userPermissions.Global.CanUpdateSpaces)
                .SetProperty(p => p.CanDeleteSpaces, userPermissions.Global.CanDeleteSpaces)
                
                .SetProperty(p => p.CanCreateEpics, userPermissions.Global.CanCreateEpics)
                .SetProperty(p => p.CanUpdateEpics, userPermissions.Global.CanUpdateEpics)
                .SetProperty(p => p.CanDeleteEpics, userPermissions.Global.CanDeleteEpics)
                
                .SetProperty(p => p.CanCreateIssues, userPermissions.Global.CanCreateIssues)
                .SetProperty(p => p.CanUpdateIssues, userPermissions.Global.CanUpdateIssues)
                .SetProperty(p => p.CanDeleteIssues, userPermissions.Global.CanDeleteIssues)
                , cancellationToken);

        var organizationData = await context.OrganizationUsers
            .Where(x => x.Id == organizationUserId)
            .Select(x => new { x.OrganizationId })
            .FirstAsyncEF(cancellationToken);

        var spaceIdsByKeys = await context.Spaces
            .Where(x => x.OrganizationId == organizationData.OrganizationId)
            .Where(x => userPermissions.Direct.Select(p => p.Key).Contains(x.Key))
            .ToDictionaryAsyncEF(x => x.Key, x => x.Id, cancellationToken);
        
        // Set spaces direct permissions
        foreach (var spaceLevel in userPermissions.Direct)
        {
            var spacePermission = new DirectSpacePermission
            {
                OrganizationUserId = organizationUserId,
                SpaceId = spaceIdsByKeys[spaceLevel.Key],
                CanRead = spaceLevel.Value.CanRead,
                CanUpdate = spaceLevel.Value.CanUpdate,
                CanDelete = spaceLevel.Value.CanDelete,
                CanCreateEpics = spaceLevel.Value.CanCreateEpics,
                CanUpdateEpics = spaceLevel.Value.CanUpdateEpics,
                CanDeleteEpics = spaceLevel.Value.CanDeleteEpics,
                CanCreateIssues = spaceLevel.Value.CanCreateIssues,
                CanUpdateIssues = spaceLevel.Value.CanUpdateIssues,
                CanDeleteIssues = spaceLevel.Value.CanDeleteIssues,
            };

            context.DirectSpacePermissions.Add(spacePermission);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserPermissions> GetUserPermissions(long organizationUserId, CancellationToken cancellationToken)
    {
        var organizationAccessLevel = await context.OrganizationUsers
            .Where(x => x.Id == organizationUserId)
            .Select(x => new 
            {
                x.AdminAccessLevel,
                x.CanCreateRetros,
                x.CanCreateSpaces,
                x.CanUpdateSpaces,
                x.CanDeleteSpaces,
                x.CanCreateEpics,
                x.CanUpdateEpics,
                x.CanDeleteEpics,
                x.CanCreateIssues,
                x.CanUpdateIssues,
                x.CanDeleteIssues,
                x.CanRead,
            })
            .FirstOrThrowNotFoundEFAsync($"User: {organizationUserId} is not found in organization", cancellationToken);

        var spaceAccessLevels = await context.DirectSpacePermissions
            .Where(x => x.OrganizationUserId == organizationUserId)
            .Select(x => new
            {
                SpaceKey = x.Space!.Key,
                AccessLevel = new DirectSpaceAccessLevel
                {
                    CanCreateEpics = x.CanCreateEpics,
                    CanUpdateEpics = x.CanUpdateEpics,
                    CanDeleteEpics = x.CanDeleteEpics,
                    CanCreateIssues = x.CanCreateIssues,
                    CanUpdateIssues = x.CanUpdateIssues,
                    CanDeleteIssues = x.CanDeleteIssues,
                    CanRead = x.CanRead,
                    CanDelete = x.CanDelete,
                    CanUpdate = x.CanUpdate,
                },
            })
            .ToDictionaryAsyncEF(x => x.SpaceKey, x => x.AccessLevel, cancellationToken);

        var permissions = new UserPermissions
        {
            Admin = organizationAccessLevel.AdminAccessLevel,
            Global = new GlobalAccessLevels
            {
                CanCreateRetros = organizationAccessLevel.CanCreateRetros,
                CanCreateEpics = organizationAccessLevel.CanCreateEpics,
                CanUpdateEpics = organizationAccessLevel.CanUpdateEpics,
                CanDeleteEpics = organizationAccessLevel.CanDeleteEpics,
                CanCreateIssues = organizationAccessLevel.CanCreateIssues,
                CanUpdateIssues = organizationAccessLevel.CanUpdateIssues,
                CanCreateSpaces =  organizationAccessLevel.CanCreateSpaces,
                CanUpdateSpaces = organizationAccessLevel.CanUpdateSpaces,
                CanDeleteSpaces = organizationAccessLevel.CanDeleteSpaces,
                CanDeleteIssues =  organizationAccessLevel.CanDeleteIssues,
                CanRead = organizationAccessLevel.CanRead,
            },
            Direct = spaceAccessLevels
        };
        
        NormalizePermissions(permissions);

        return permissions;
    }

    public async Task<PermittableSpace[]> GetPermittableEntities(
        long organizationId,
        CancellationToken cancellationToken)
    {
        var spaces = await context.Spaces
            .Where(x => x.OrganizationId == organizationId)
            .ToDictionaryAsyncEF(x => x.Key, x => new { x.Name, x.Color, x.IsDefault }, cancellationToken);

        return spaces
            .Select(s => new PermittableSpace(
                s.Key,
                s.Value.Name,
                s.Value.Color,
                s.Value.IsDefault))
            .ToArray();
    }
    
    public async Task UpdatePreferences(
        long organizationId,
        Guid userId,
        Action<UpdateSettersBuilder<UserOrganizationPreferences>> updateSetters,
        CancellationToken cancellationToken)
    {
        var updatedCount = await context.UserOrganizationPreferences
            .Where(x => x.UserId == userId)
            .Where(x => x.OrganizationId == organizationId)
            .ExecuteUpdateAsync(updateSetters, cancellationToken);
        
        if (updatedCount > 0)
            return;
        
        // The first settings setup
        var preferences = GetDefaultPreferences(organizationId, userId);
        context.Add(preferences);
        
        await context.SaveChangesAsync(cancellationToken);
        await context.UserOrganizationPreferences
            .Where(x => x.UserId == userId)
            .Where(x => x.OrganizationId == organizationId)
            .ExecuteUpdateAsync(updateSetters, cancellationToken);
    }

    public async Task<UserOrganizationPreferencesResponse> GetPreferences(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preferences = await context.UserOrganizationPreferences
            .Where(x => x.UserId == userId)
            .Where(x => x.OrganizationId == organizationId)
            .FirstOrDefaultAsyncEF(cancellationToken)
                ?? GetDefaultPreferences(organizationId, userId);

        return new UserOrganizationPreferencesResponse
        {
            SelectedSpaceId = preferences.SelectedSpaceId,
        };
    }

    public async Task<long> CreateAttribute(
        long organizationId,
        string name,
        string color,
        AttributeType attributeType,
        string[]? listValues,
        CancellationToken cancellationToken)
    {
        var attribute = new Attribute
        {
            Name = name,
            AttributeType = attributeType,
            Color = color,
            OrganizationId = organizationId,
            AttributeListValues = listValues?
                .Select(x => new AttributeListValue
                {
                    Value = x
                })
                .ToList()
        };
        
        context.Add(attribute);
        await context.SaveChangesAsync(cancellationToken);
        
        return attribute.Id;
    }

    public async Task UpdateAttribute(
        long attributeId,
        string name,
        string color,
        UpdateAttributeListValueRequest[]? listValues,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();
        
        var updatedCount = await context.Attributes
            .Where(x => x.Id == attributeId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, name)
                .SetProperty(x => x.Color, color),
            cancellationToken);

        if (updatedCount == 0)
            return;

        if (listValues == null || listValues.Length == 0)
        {
            await context.AttributeListValues
                .Where(x => x.AttributeId == attributeId)
                .ExecuteDeleteAsync(cancellationToken);
            
            return;
        }

        var oldListValues = await context.AttributeListValues
            .Where(x => x.AttributeId == attributeId)
            .ToArrayAsyncEF(cancellationToken);
        
        var oldListValuesDictionary = oldListValues.ToDictionary(x => x.Id, x => x);

        foreach (var listValue in listValues)
        {
            // Update old
            if (listValue.Id.HasValue && oldListValuesDictionary.TryGetValue(listValue.Id.Value, out var oldValue))
            {
                oldValue.Value = listValue.Name;
                context.Attach(oldValue).State = EntityState.Modified;
            }
            // Insert new
            else
                context.Add(new AttributeListValue
                {
                    Value = listValue.Name,
                    AttributeId = attributeId,
                });
        }
        
        // Delete old
        var allListValueIds = listValues
            .Where(x => x.Id.HasValue)
            .Select(x => x.Id!.Value);

        var toDelete = oldListValuesDictionary.Keys
            .Except(allListValueIds)
            .ToArray();
        
        if (toDelete.Length > 0)
            await context.AttributeListValues
                .Where(x => ((IEnumerable<long>)toDelete).Contains(x.Id))
                .ExecuteDeleteAsync(cancellationToken);
        
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteAttribute(long attributeId, CancellationToken cancellationToken)
    {
        return context.Attributes
            .Where(x => x.Id == attributeId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static UserOrganizationPreferences GetDefaultPreferences(
        long organizationId,
        Guid userId)
    {
        return new UserOrganizationPreferences
        {
            UserId = userId,
            OrganizationId = organizationId,
        };
    }

    /// <summary>
    /// Add explicit implicitly added permissions, e.g. Read is always need when Write is active.
    /// </summary>
    private void NormalizePermissions(UserPermissions permissions)
    {
        // Global access setup
        var global = permissions.Global;

        // Add read when any other permission is granted
        if (global.CanCreateSpaces || global.CanUpdateSpaces || global.CanDeleteSpaces
            || global.CanCreateEpics || global.CanUpdateEpics || global.CanDeleteEpics
            || global.CanCreateIssues || global.CanUpdateIssues ||  global.CanDeleteIssues)
        {
            global.CanRead = true;
        }
        
        // Use inheritance from top to the bottom levels
        if (global.CanCreateSpaces)
            global.CanCreateEpics = true;
        
        if (global.CanCreateEpics)
            global.CanCreateIssues = true;
        
        if (global.CanUpdateSpaces)
            global.CanUpdateEpics = true;
        
        if (global.CanUpdateEpics)
            global.CanUpdateIssues = true;
        
        if (global.CanDeleteSpaces)
            global.CanDeleteEpics = true;
        
        if (global.CanDeleteEpics)
            global.CanDeleteIssues = true;
        
        // Direct access setup. Do the same, but see also to the global level to merge access level.
        foreach (var directSpaceAccess in permissions.Direct)
        {
            var direct = directSpaceAccess.Value;
            
            // inheritance from global
            if (global.CanRead)
                direct.CanRead = true;
            
            if (global.CanUpdateSpaces)
                direct.CanUpdate = true;
            
            if (global.CanDeleteSpaces)
                direct.CanDelete = true;
            
            if (global.CanCreateEpics)
                direct.CanCreateEpics = true;
            
            if (global.CanUpdateEpics)
                direct.CanUpdateEpics = true;
            
            if (global.CanDeleteEpics)
                direct.CanDeleteEpics = true;
            
            if (global.CanCreateIssues)
                direct.CanCreateIssues = true;
            
            if (global.CanUpdateIssues)
                direct.CanUpdateIssues = true;
            
            if (global.CanDeleteIssues)
                direct.CanDeleteIssues = true;
            
            // inheritance from bottom to top levels
            if (direct.CanUpdate || direct.CanDelete
                || direct.CanCreateEpics || direct.CanUpdateEpics || direct.CanDeleteEpics
                || direct.CanCreateIssues || direct.CanUpdateIssues ||  direct.CanDeleteIssues)
                direct.CanRead = true;
            
            if (direct.CanCreateEpics)
                direct.CanCreateIssues = true;
            
            if (direct.CanUpdate)
                direct.CanUpdateEpics = true;
            
            if (direct.CanUpdateEpics)
                direct.CanUpdateIssues = true;
            
            if (direct.CanDelete)
                direct.CanDeleteEpics = true;
            
            if (direct.CanDeleteEpics)
                direct.CanDeleteIssues = true;
        }
    }
}

public record UserPermissions
{
    public GlobalAccessLevels Global { get; set; } = new();
    public Dictionary<string, DirectSpaceAccessLevel> Direct { get; set; } = new();
    public AdminAccessLevel Admin { get; set; }
}

public record GlobalAccessLevels
{
    public bool CanRead { get; set; }
    public bool CanCreateRetros { get; set; }
    public bool CanCreateSpaces { get; set; }
    public bool CanUpdateSpaces { get; set; }
    public bool CanDeleteSpaces { get; set; }
    public bool CanCreateEpics { get; set; }
    public bool CanUpdateEpics { get; set; }
    public bool CanDeleteEpics { get; set; }
    public bool CanCreateIssues { get; set; }
    public bool CanUpdateIssues { get; set; }
    public bool CanDeleteIssues { get; set; }
}

public record DirectSpaceAccessLevel
{
    public bool CanRead { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool CanCreateEpics { get; set; }
    public bool CanUpdateEpics { get; set; }
    public bool CanDeleteEpics { get; set; }
    public bool CanCreateIssues { get; set; }
    public bool CanUpdateIssues { get; set; }
    public bool CanDeleteIssues { get; set; }
}

public record PermittableSpace(string Key, string Name, string Color, bool IsDefault);

public record CreateOrganizationResponse
{
    public long Id { get; set; }
    public required string Slug { get; set; }
    public required string SlugPostfix { get; set; }
}

public record UserOrganizationPreferencesResponse
{
    public long? SelectedSpaceId { get; init; }
}

public record UpdateAttributeListValueRequest
{
    public long? Id { get; set; }
    public required string Name { get; set; }
}

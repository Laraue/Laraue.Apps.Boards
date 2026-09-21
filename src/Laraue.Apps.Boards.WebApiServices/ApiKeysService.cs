using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DataAccess.Extensions;
using Laraue.Core.Exceptions.Web;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Self-service API key management - a member creates/lists/revokes only their own keys for the
/// organization in <see cref="ApiKeyRequest.AuthData"/>. There's no admin-gated "manage everyone's
/// keys" surface (yet) - a key only ever grants what its own creator could already do, so there's
/// nothing for an admin to additionally authorize here.
/// </summary>
public interface IApiKeysService
{
    Task<CreateApiKeyResponse> Create(CreateApiKeyRequest request, CancellationToken cancellationToken);

    Task<ShortPaginatedResult<ApiKeyDto>> GetAll(GetApiKeysRequest request, CancellationToken cancellationToken);

    Task Revoke(RevokeApiKeyRequest request, CancellationToken cancellationToken);
}

public class ApiKeysService(
    DatabaseContext context,
    ICoreApiKeysService coreApiKeysService)
    : IApiKeysService
{
    public async Task<CreateApiKeyResponse> Create(CreateApiKeyRequest request, CancellationToken cancellationToken)
    {
        var result = await coreApiKeysService.CreateAsync(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            request.Name,
            cancellationToken);

        return new CreateApiKeyResponse
        {
            Id = result.Id,
            RawKey = result.RawKey,
        };
    }

    public Task<ShortPaginatedResult<ApiKeyDto>> GetAll(GetApiKeysRequest request, CancellationToken cancellationToken)
    {
        return context.ApiKeys
            .Where(x => x.OrganizationId == request.AuthData.OrganizationId && x.CreatedByUserId == request.AuthData.UserId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ApiKeyDto
            {
                Id = x.Id,
                Name = x.Name,
                KeyPrefix = x.KeyPrefix,
                CreatedAt = x.CreatedAt,
                LastUsedAt = x.LastUsedAt,
                RevokedAt = x.RevokedAt,
            })
            .ShortPaginateEFAsync(request.Pagination, cancellationToken);
    }

    public async Task Revoke(RevokeApiKeyRequest request, CancellationToken cancellationToken)
    {
        var revoked = await coreApiKeysService.RevokeAsync(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            request.Id,
            cancellationToken);

        if (!revoked)
        {
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "ApiKey", request.Id));
        }
    }
}

public record ApiKeyRequest
{
    public OrganizationAuthData AuthData { get; set; }
}

public record CreateApiKeyRequest : ApiKeyRequest
{
    [MaxLength(64)]
    [MinLength(1)]
    public required string Name { get; set; }
}

public record CreateApiKeyResponse
{
    public required Guid Id { get; set; }

    /// <summary>
    /// The raw secret - shown to the caller this one time only, never retrievable again.
    /// </summary>
    public required string RawKey { get; set; }
}

public record ApiKeyDto
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string KeyPrefix { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public record RevokeApiKeyRequest : ApiKeyRequest
{
    public required Guid Id { get; set; }
}

public record GetApiKeysRequest : ApiKeyRequest, IPaginatedRequest
{
    public required PaginationData Pagination { get; set; }
}

using System.Security.Cryptography;
using System.Text;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Long-lived, self-service credentials that let a program authenticate as the creating user
/// within one organization (e.g. an MCP client) - see <see cref="ApiKey"/>. Only covers the
/// mutating operations - listing a user's own keys is a plain read and stays in
/// <c>ApiKeysService</c>, querying <c>DatabaseContext</c> directly (see AGENTS.md's "Service
/// layering" section). Callers are trusted to have already decided that the given caller id may
/// act on the given key - this service only scopes writes to it (a member manages only their own
/// keys), it doesn't decide whether that's allowed.
/// </summary>
public interface ICoreApiKeysService
{
    Task<ApiKeyCreationResult> CreateAsync(
        long organizationId,
        Guid createdByUserId,
        string name,
        CancellationToken cancellationToken);

    Task<bool> RevokeAsync(
        long organizationId,
        Guid callerId,
        Guid apiKeyId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a raw secret to the organization/user it authenticates as, or null if the key
    /// doesn't exist or has been revoked. Best-effort bumps <see cref="ApiKey.LastUsedAt"/> on
    /// success.
    /// </summary>
    Task<ApiKeyPrincipal?> ValidateAsync(string rawKey, CancellationToken cancellationToken);
}

public sealed record ApiKeyPrincipal(long OrganizationId, Guid UserId);

/// <summary>
/// <see cref="RawKey"/> is only ever available here, at creation time - it's never stored or
/// retrievable again.
/// </summary>
public sealed record ApiKeyCreationResult(Guid Id, string RawKey);

public class CoreApiKeysService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider)
    : ICoreApiKeysService
{
    private const int KeyPrefixLength = 12;

    public async Task<ApiKeyCreationResult> CreateAsync(
        long organizationId,
        Guid createdByUserId,
        string name,
        CancellationToken cancellationToken)
    {
        var rawKey = StringGenerator.GenerateApiKey();

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            KeyHash = Hash(rawKey),
            KeyPrefix = rawKey[..KeyPrefixLength],
            CreatedByUserId = createdByUserId,
            CreatedAt = dateTimeProvider.UtcNow,
        };

        context.ApiKeys.Add(apiKey);
        await context.SaveChangesAsync(cancellationToken);

        return new ApiKeyCreationResult(apiKey.Id, rawKey);
    }

    public async Task<bool> RevokeAsync(
        long organizationId,
        Guid callerId,
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        var updatedCount = await context.ApiKeys
            .Where(x => x.Id == apiKeyId
                && x.OrganizationId == organizationId
                && x.CreatedByUserId == callerId
                && x.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.RevokedAt, dateTimeProvider.UtcNow),
                cancellationToken);

        return updatedCount > 0;
    }

    public async Task<ApiKeyPrincipal?> ValidateAsync(string rawKey, CancellationToken cancellationToken)
    {
        var keyHash = Hash(rawKey);

        var apiKey = await context.ApiKeys
            .Where(x => x.KeyHash == keyHash && x.RevokedAt == null)
            .Select(x => new { x.Id, x.OrganizationId, x.CreatedByUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (apiKey is null)
            return null;

        await context.ApiKeys
            .Where(x => x.Id == apiKey.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.LastUsedAt, dateTimeProvider.UtcNow),
                cancellationToken);

        return new ApiKeyPrincipal(apiKey.OrganizationId, apiKey.CreatedByUserId);
    }

    private static string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexStringLower(bytes);
    }
}

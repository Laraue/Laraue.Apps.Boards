using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Storage.Internal.Contracts;
using Microsoft.EntityFrameworkCore;
using StorageService = Laraue.Apps.Storage.Internal.Contracts.StorageService;

namespace Laraue.Apps.Boards.Services.Storage;

/// <summary>
/// An organization's file storage (BRD-340): the quota comes from Billing, the usage from Laraue.Apps.Storage, which
/// counts it per owner. Uploads count toward the organization they're made in: a personal organization uses its
/// owner's personal plan, a team organization its own plan, whoever uploads.
/// </summary>
public interface IStorageQuotaService
{
    /// <summary>
    /// The organization's quota in bytes: its plan's storage plus the storage add-on.
    /// </summary>
    Task<long> GetQuotaBytes(long organizationId, CancellationToken cancellationToken);

    /// <summary>
    /// What the organization's uploads take now, originals and previews, in bytes.
    /// </summary>
    Task<long> GetUsageBytes(long organizationId, CancellationToken cancellationToken);
}

public class StorageQuotaService(
    DatabaseContext context,
    IBillingSubscriptionClient subscriptionClient,
    StorageService.StorageServiceClient storageClient)
    : IStorageQuotaService
{
    public async Task<long> GetQuotaBytes(long organizationId, CancellationToken cancellationToken)
    {
        // The owner's id decides a personal organization's plan, so another member's upload there still counts
        // against the owner's quota. A team organization's plan doesn't depend on the user.
        var ownerId = await context.ActiveOrganizations()
            .Where(o => o.Id == organizationId)
            .Select(o => o.OwnerId)
            .SingleAsync(cancellationToken);

        var subscription = await subscriptionClient.GetActiveSubscriptionAsync(
            organizationId,
            ownerId,
            cancellationToken);

        return subscription.StorageQuotaBytes;
    }

    public async Task<long> GetUsageBytes(long organizationId, CancellationToken cancellationToken)
    {
        var response = await storageClient.GetUsageAsync(
            new GetUsageRequest { Owner = new Owner { OrganizationId = organizationId } },
            cancellationToken: cancellationToken);

        return response.UsageBytes;
    }
}

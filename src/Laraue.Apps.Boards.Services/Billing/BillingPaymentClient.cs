using Grpc.Core;
using Laraue.Apps.Billing.Internal.Contracts;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Starts a payment in the Billing service. Like <see cref="IBillingTokenClient"/>, it takes a plain
/// <c>organizationId</c> and resolves Billing's personal-vs-team fork from live data, so a caller
/// never needs to know which kind of organization it is. The price and the payment provider are
/// Billing's business: the caller names the item and gets back the address to send the customer to.
/// </summary>
public interface IBillingPaymentClient
{
    /// <summary>
    /// Creates a payment for <paramref name="itemId"/> (a tariff or a token pack, see
    /// <paramref name="kind"/>) covering <paramref name="organizationId"/>, paid by
    /// <paramref name="userId"/>. Throws a <see cref="BadRequestException"/> when Billing refuses
    /// the item (unknown, not for sale, wrong kind of tariff for this organization, unsupported currency).
    /// <paramref name="storage"/> adds a storage add-on to a tariff (BRD-341) and passes the organization's usage:
    /// when the tariff and add-on would give less storage than that, nothing is created and
    /// <see cref="StorageOverLimitException"/> is thrown.
    /// </summary>
    Task<BillingCheckout> CreateCheckoutAsync(
        long organizationId,
        Guid userId,
        BillingItemKind kind,
        Guid itemId,
        string currencyCode,
        BillingStorageCheckout? storage,
        CancellationToken cancellationToken);
}

public sealed record BillingCheckout(Guid PaymentId, string Url);

/// <summary>
/// Storage bought with a tariff: the add-on (null for none) and the organization's current usage, which Billing
/// checks the new storage against.
/// </summary>
public sealed record BillingStorageCheckout(Guid? AddonId, long UsageBytes);

/// <summary>
/// Billing refused a checkout because the organization uses more storage than the tariff and add-on would give.
/// </summary>
public sealed class StorageOverLimitException(long usageBytes, long quotaBytes)
    : Exception($"{usageBytes} bytes of storage are used, more than the {quotaBytes} bytes the checkout would give.")
{
    public long UsageBytes { get; } = usageBytes;

    public long QuotaBytes { get; } = quotaBytes;
}

public enum BillingItemKind
{
    Subscription,
    TokenPack,
}

public class BillingPaymentClient(DatabaseContext context, PaymentService.PaymentServiceClient client)
    : IBillingPaymentClient
{
    public async Task<BillingCheckout> CreateCheckoutAsync(
        long organizationId,
        Guid userId,
        BillingItemKind kind,
        Guid itemId,
        string currencyCode,
        BillingStorageCheckout? storage,
        CancellationToken cancellationToken)
    {
        var organization = await context.ActiveOrganizations()
            .Where(o => o.Id == organizationId)
            .Select(o => new { o.Type, o.BillingId })
            .SingleAsync(cancellationToken);

        var itemKind = kind switch
        {
            BillingItemKind.Subscription => PaymentItemKind.Subscription,
            BillingItemKind.TokenPack => PaymentItemKind.TokenPack,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };

        var storageCheckout = storage is null
            ? null
            : new StorageCheckout
            {
                AddonId = storage.AddonId?.ToString() ?? string.Empty,
                UsageBytes = storage.UsageBytes,
            };

        try
        {
            var response = organization.Type == OrganizationType.Personal
                ? await client.CreatePersonalCheckoutAsync(
                    new CreatePersonalCheckoutRequest
                    {
                        UserId = userId.ToString(),
                        Kind = itemKind,
                        ItemId = itemId.ToString(),
                        CurrencyCode = currencyCode,
                        Storage = storageCheckout,
                    },
                    cancellationToken: cancellationToken)
                : await client.CreateOrganizationCheckoutAsync(
                    new CreateOrganizationCheckoutRequest
                    {
                        OrganizationId = organization.BillingId!.Value.ToString(),
                        UserId = userId.ToString(),
                        Kind = itemKind,
                        ItemId = itemId.ToString(),
                        CurrencyCode = currencyCode,
                        Storage = storageCheckout,
                    },
                    cancellationToken: cancellationToken);

            if (response.StorageOverLimit is { } overLimit)
            {
                throw new StorageOverLimitException(overLimit.UsageBytes, overLimit.QuotaBytes);
            }

            return new BillingCheckout(Guid.Parse(response.PaymentId), response.Url);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            throw new BadRequestException(nameof(itemId), ex.Status.Detail);
        }
    }
}

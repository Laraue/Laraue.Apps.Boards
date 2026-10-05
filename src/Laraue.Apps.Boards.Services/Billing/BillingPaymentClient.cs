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
    /// </summary>
    Task<BillingCheckout> CreateCheckoutAsync(
        long organizationId,
        Guid userId,
        BillingItemKind kind,
        Guid itemId,
        string currencyCode,
        CancellationToken cancellationToken);
}

public sealed record BillingCheckout(Guid PaymentId, string Url);

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
                    },
                    cancellationToken: cancellationToken);

            return new BillingCheckout(Guid.Parse(response.PaymentId), response.Url);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            throw new BadRequestException(nameof(itemId), ex.Status.Detail);
        }
    }
}

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.Extensions;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.Exceptions.Web;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.WebApiServices;

public record GetBillingTransactionsRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required PaginationData Pagination { get; set; }
}

/// <summary>
/// A capped resource's total allowance, how much of it has been used, and how much is left.
/// <see cref="Remaining"/> isn't always just <see cref="Limit"/> minus <see cref="Used"/> - the
/// token bucket's <see cref="Remaining"/> is the full currently-spendable balance (plan tokens
/// plus any purchased top-ups), while <see cref="Used"/> only tracks spend against the plan's own
/// grant, since a purchased pack has no single "included" figure to count against.
/// </summary>
public sealed record LimitUsage
{
    public required long Limit { get; init; }
    public required long Used { get; init; }
    public required long Remaining { get; init; }
}

/// <summary>
/// Split personal/team rather than one flat shape with an always-nullable
/// <c>FreeTeamOrganizations</c> field, since a null there would otherwise mean two different
/// things depending on context (team org - not applicable at all, vs. personal org - genuinely
/// unlimited) with nothing in the shape itself to tell them apart. Mirrors how Billing's own
/// subscription rpc already discriminates <c>LaraueBoardsPersonalSubscription</c> vs.
/// <c>LaraueBoardsTeamSubscription</c>.
/// </summary>
[JsonDerivedType(typeof(PersonalBillingSummary), "personal")]
[JsonDerivedType(typeof(TeamBillingSummary), "team")]
public abstract record BillingSummary
{
    public required string SubscriptionCode { get; init; }

    /// <summary>
    /// Whether the caller can pay for the plan - only the organization's owner can.
    /// </summary>
    public required bool CanPay { get; init; }

    /// <summary>
    /// Null means unlimited - nothing to show.
    /// </summary>
    public LimitUsage? IssuesPerMonth { get; init; }

    public required LimitUsage Tokens { get; init; }
}

public sealed record PersonalBillingSummary : BillingSummary
{
    /// <summary>
    /// Null means the plan doesn't cap it.
    /// </summary>
    public LimitUsage? FreeTeamOrganizations { get; init; }
}

public sealed record TeamBillingSummary : BillingSummary;

public sealed record BillingTransaction
{
    public required Guid Id { get; init; }
    public required TokenTransactionStatus Status { get; init; }
    public required TokenTransactionReason Reason { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public required long Delta { get; init; }
    public string? Error { get; init; }
}

public sealed record CreateCheckoutRequest
{
    /// <summary>
    /// What is being bought: a tariff or a token pack.
    /// </summary>
    public required BillingItemKind Kind { get; init; }

    /// <summary>
    /// The tariff id or the token pack id, as Billing's tariffs endpoint returns it.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// ISO 4217 code the customer pays in, e.g. <c>RUB</c>.
    /// </summary>
    [Required]
    [StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }
}

public sealed record CheckoutDto
{
    public required Guid PaymentId { get; init; }

    /// <summary>
    /// The address to send the customer to in order to pay.
    /// </summary>
    public required string Url { get; init; }
}

public sealed record TariffName
{
    public required string Name { get; init; }
}

public interface IBillingService
{
    /// <summary>
    /// Just the tariff's display name - kept separate from <see cref="GetSummary"/> (which already
    /// includes it as <c>SubscriptionCode</c>) for a caller that wants a cheap "your plan: X" label
    /// without paying for the balance/limit round trips <see cref="GetSummary"/> also does.
    /// </summary>
    Task<TariffName> GetTariffName(OrganizationAuthData authData, CancellationToken cancellationToken);

    /// <summary>
    /// Current plan and remaining token balance for the caller's organization - one round trip
    /// combining <see cref="IBillingSubscriptionClient"/> and <see cref="IBillingTokenClient"/>
    /// rather than making the frontend call two endpoints for one screen.
    /// </summary>
    Task<BillingSummary> GetSummary(OrganizationAuthData authData, CancellationToken cancellationToken);

    /// <summary>
    /// Paginated token transaction ledger - kept separate from <see cref="GetSummary"/> since it's
    /// a different shape of request (paginated) serving a different part of the UI (a history list
    /// vs. a one-shot balance card).
    /// </summary>
    Task<ShortPaginatedResult<BillingTransaction>> GetTransactions(
        GetBillingTransactionsRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Starts a payment for the caller's organization and returns where to send the customer. Only
    /// the organization's owner pays for it.
    /// </summary>
    Task<CheckoutDto> CreateCheckout(
        OrganizationAuthData authData,
        CreateCheckoutRequest request,
        CancellationToken cancellationToken);
}

public class BillingService(
    IBillingSubscriptionClient subscriptionClient,
    IBillingTokenClient tokenClient,
    IIssueMonthlyCountService issueMonthlyCountService,
    IUsageLimitService usageLimitService,
    IBillingPaymentClient paymentClient,
    IAccessService accessService,
    ILogger<BillingService> logger,
    IDateTimeProvider dateTimeProvider) : IBillingService
{
    public async Task<CheckoutDto> CreateCheckout(
        OrganizationAuthData authData,
        CreateCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (!await accessService.IsOrganizationOwner(authData, cancellationToken))
        {
            logger.LogWarning(
                "User {UserId} tried to pay for organization {OrganizationId} but is not its owner",
                authData.UserId,
                authData.OrganizationId);

            throw new ForbiddenException(ErrorMessages.OnlyOrganizationOwnerCanPay);
        }

        logger.LogInformation(
            "Starting a payment: {Kind} {ItemId} in {CurrencyCode} for organization {OrganizationId} by owner {UserId}",
            request.Kind,
            request.ItemId,
            request.CurrencyCode,
            authData.OrganizationId,
            authData.UserId);

        var checkout = await paymentClient.CreateCheckoutAsync(
            authData.OrganizationId,
            authData.UserId,
            request.Kind,
            request.ItemId,
            request.CurrencyCode.ToUpperInvariant(),
            cancellationToken);

        logger.LogInformation(
            "Payment {PaymentId} started for organization {OrganizationId}",
            checkout.PaymentId,
            authData.OrganizationId);

        return new CheckoutDto { PaymentId = checkout.PaymentId, Url = checkout.Url };
    }

    public async Task<TariffName> GetTariffName(OrganizationAuthData authData, CancellationToken cancellationToken)
    {
        var name = await subscriptionClient.GetTariffNameAsync(
            authData.OrganizationId, authData.UserId, cancellationToken);

        return new TariffName { Name = name };
    }

    public async Task<BillingSummary> GetSummary(OrganizationAuthData authData, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionClient.GetActiveSubscriptionAsync(
            authData.OrganizationId, authData.UserId, cancellationToken);

        var balance = await tokenClient.GetBalanceAsync(
            authData.OrganizationId, authData.UserId, cancellationToken);

        var canPay = await accessService.IsOrganizationOwner(authData, cancellationToken);

        LimitUsage? issuesPerMonth = null;
        if (subscription.LimitIssuesPerMonth is { } issuesLimit)
        {
            var now = dateTimeProvider.UtcNow;
            var issuesUsed = await issueMonthlyCountService.GetCount(
                authData.OrganizationId, now.Year, now.Month, cancellationToken);

            issuesPerMonth = new LimitUsage
            {
                Limit = issuesLimit,
                Used = issuesUsed,
                Remaining = Math.Max(0, issuesLimit - issuesUsed),
            };
        }

        // What is left of the plan's own allowance: a paid plan's tokens are in the subscription bucket,
        // a Free plan's monthly allowance in the free one (its subscription bucket stays empty), so both
        // count. Purchased packs are not part of the plan, they only add to the remaining total.
        var planTokensLeft = balance.SubscriptionTokensCount + balance.FreeTokensCount;
        var tokensUsed = Math.Max(0, subscription.IncludedTokensCount - planTokensLeft);
        var tokensRemaining = balance.SubscriptionTokensCount + balance.FreeTokensCount + balance.PurchasedTokensCount;

        var tokens = new LimitUsage
        {
            Limit = subscription.IncludedTokensCount,
            Used = tokensUsed,
            Remaining = tokensRemaining,
        };

        if (!subscription.IsPersonal)
        {
            return new TeamBillingSummary
            {
                SubscriptionCode = subscription.Code,
                CanPay = canPay,
                IssuesPerMonth = issuesPerMonth,
                Tokens = tokens,
            };
        }

        LimitUsage? freeTeamOrganizations = null;
        if (subscription.LimitFreeTeamOrganizationsCount is { } organizationsLimit)
        {
            var organizationsUsed = await usageLimitService.GetOwnedTeamOrganizationsCountAsync(
                authData.UserId, cancellationToken);

            freeTeamOrganizations = new LimitUsage
            {
                Limit = organizationsLimit,
                Used = organizationsUsed,
                Remaining = Math.Max(0, organizationsLimit - organizationsUsed),
            };
        }

        return new PersonalBillingSummary
        {
            SubscriptionCode = subscription.Code,
            CanPay = canPay,
            IssuesPerMonth = issuesPerMonth,
            Tokens = tokens,
            FreeTeamOrganizations = freeTeamOrganizations,
        };
    }

    public async Task<ShortPaginatedResult<BillingTransaction>> GetTransactions(
        GetBillingTransactionsRequest request,
        CancellationToken cancellationToken)
    {
        var page = await tokenClient.GetTransactionsAsync(
            request.AuthData.OrganizationId,
            request.AuthData.UserId,
            request.Pagination,
            cancellationToken);

        return page.MapTo(item => new BillingTransaction
        {
            Id = item.Id,
            Status = item.Status,
            Reason = item.Reason,
            CreatedAt = item.CreatedAt,
            FinishedAt = item.FinishedAt,
            Delta = item.Delta,
            Error = item.Error,
        });
    }
}

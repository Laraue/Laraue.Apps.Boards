using System.Text.Json.Serialization;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;

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
}

public class BillingService(
    IBillingSubscriptionClient subscriptionClient,
    IBillingTokenClient tokenClient,
    IIssueMonthlyCountService issueMonthlyCountService,
    IUsageLimitService usageLimitService,
    IDateTimeProvider dateTimeProvider) : IBillingService
{
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

        var tokensUsed = Math.Max(0, subscription.IncludedTokensCount - balance.SubscriptionTokensCount);
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

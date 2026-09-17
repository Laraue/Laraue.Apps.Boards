using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.DataAccess.Extensions;

namespace Laraue.Apps.Boards.WebApiServices;

public record GetBillingTransactionsRequest : IPaginatedRequest
{
    public OrganizationAuthData AuthData { get; set; }
    public required PaginationData Pagination { get; set; }
}

public sealed record BillingSummary
{
    public required string SubscriptionCode { get; init; }
    public int? LimitIssuesPerMonth { get; init; }
    public int? LimitFreeTeamOrganizationsCount { get; init; }
    public required long FreeTokensCount { get; init; }
    public required long SubscriptionTokensCount { get; init; }
    public required long PurchasedTokensCount { get; init; }
}

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

public interface IBillingService
{
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
    IBillingTokenClient tokenClient) : IBillingService
{
    public async Task<BillingSummary> GetSummary(OrganizationAuthData authData, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionClient.GetActiveSubscriptionAsync(
            authData.OrganizationId, authData.UserId, cancellationToken);

        var balance = await tokenClient.GetBalanceAsync(
            authData.OrganizationId, authData.UserId, cancellationToken);

        return new BillingSummary
        {
            SubscriptionCode = subscription.Code,
            LimitIssuesPerMonth = subscription.LimitIssuesPerMonth,
            LimitFreeTeamOrganizationsCount = subscription.LimitFreeTeamOrganizationsCount,
            FreeTokensCount = balance.FreeTokensCount,
            SubscriptionTokensCount = balance.SubscriptionTokensCount,
            PurchasedTokensCount = balance.PurchasedTokensCount,
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

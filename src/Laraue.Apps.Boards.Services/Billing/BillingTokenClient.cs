using Grpc.Core;
using Laraue.Apps.Billing.Internal.Contracts;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.EntityFrameworkCore;
// Billing's wire contract has its own TokenTransactionStatus/TokenTransactionReason enums, colliding
// with this file's own Boards-side equivalents below - alias to keep both usable unqualified.
using ContractsTokenTransactionStatus = Laraue.Apps.Billing.Internal.Contracts.TokenTransactionStatus;
using ContractsTokenTransactionReason = Laraue.Apps.Billing.Internal.Contracts.TokenTransactionReason;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Reserve/commit/cancel AI token spend against the Billing service. The one public entry point
/// for reserving, <see cref="ReserveTokensAsync"/>, takes a plain <c>organizationId</c> rather than
/// exposing Billing's own personal-vs-organization rpc split - Boards' callers (an AI-summarize
/// call site) don't know in advance whether a given organization is personal or a real team, so
/// resolving that fork from live data is this wrapper's job, not a flag the caller has to supply.
/// This doesn't reintroduce the "isOrganization bool" shape Billing itself rejected for its own
/// wire contract - that decision was about a surface with multiple callers who each already know
/// which kind they mean; here there's exactly one caller-facing operation ("spend tokens for this
/// org's AI call") and the personal/team resolution is an internal implementation detail.
/// </summary>
public interface IBillingTokenClient
{
    /// <summary>
    /// Reserves tokens for <paramref name="organizationId"/>, billing to
    /// <paramref name="userId"/> if the organization is personal or to the organization's
    /// <see cref="Organization.BillingId"/> if it's a real team. Throws
    /// <see cref="InsufficientTokenBalanceException"/> if Billing rejects the reservation for
    /// insufficient balance.
    /// </summary>
    Task<Guid> ReserveTokensAsync(
        long organizationId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken);

    Task CommitTokensSpentAsync(Guid tokenTransactionId, int actualOutputTokensCount, CancellationToken cancellationToken);

    Task CancelTokensReservationAsync(Guid tokenTransactionId, string error, CancellationToken cancellationToken);

    /// <summary>
    /// Current remaining balance for <paramref name="organizationId"/> - same personal/team
    /// resolution as <see cref="ReserveTokensAsync"/>.
    /// </summary>
    Task<TokenBalance> GetBalanceAsync(long organizationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Paginated token transaction ledger for <paramref name="organizationId"/>'s billed entity
    /// (the user, if personal, or the organization's own <see cref="Organization.BillingId"/>).
    /// </summary>
    Task<ShortPaginatedResult<TokenTransactionItem>> GetTransactionsAsync(
        long organizationId,
        Guid userId,
        PaginationData pagination,
        CancellationToken cancellationToken);
}

public sealed record TokenBalance
{
    public required long FreeTokensCount { get; init; }
    public required long SubscriptionTokensCount { get; init; }
    public required long PurchasedTokensCount { get; init; }
}

public sealed record TokenTransactionItem
{
    public required Guid Id { get; init; }
    public required TokenTransactionStatus Status { get; init; }
    public required TokenTransactionReason Reason { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public required long Delta { get; init; }
    public string? Error { get; init; }
}

public enum TokenTransactionStatus
{
    Started,
    Canceled,
    Confirmed,
}

public enum TokenTransactionReason
{
    TariffGrant,
    DailyGrant,
    Purchase,
    Expiry,
    Spend,
}

public class BillingTokenClient(DatabaseContext context, TokenService.TokenServiceClient client) : IBillingTokenClient
{
    public async Task<Guid> ReserveTokensAsync(
        long organizationId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationBillingInfoAsync(organizationId, cancellationToken);

        try
        {
            var response = organization.Type == OrganizationType.Personal
                ? await client.ReservePersonalTokensAsync(
                    new ReservePersonalTokensRequest
                    {
                        UserId = userId.ToString(),
                        InputTokensCount = inputTokensCount,
                        MaxOutputTokensCount = maxOutputTokensCount,
                    },
                    cancellationToken: cancellationToken)
                : await client.ReserveOrganizationTokensAsync(
                    new ReserveOrganizationTokensRequest
                    {
                        OrganizationId = organization.BillingId!.Value.ToString(),
                        InputTokensCount = inputTokensCount,
                        MaxOutputTokensCount = maxOutputTokensCount,
                    },
                    cancellationToken: cancellationToken);

            return Guid.Parse(response.TokenTransactionId);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition)
        {
            throw new InsufficientTokenBalanceException(ex.Status.Detail);
        }
    }

    public async Task<TokenBalance> GetBalanceAsync(long organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationBillingInfoAsync(organizationId, cancellationToken);

        var response = organization.Type == OrganizationType.Personal
            ? await client.GetPersonalTokenBalanceAsync(
                new GetPersonalTokenBalanceRequest { UserId = userId.ToString() },
                cancellationToken: cancellationToken)
            : await client.GetOrganizationTokenBalanceAsync(
                new GetOrganizationTokenBalanceRequest { OrganizationId = organization.BillingId!.Value.ToString() },
                cancellationToken: cancellationToken);

        return new TokenBalance
        {
            FreeTokensCount = response.FreeTokensCount,
            SubscriptionTokensCount = response.SubscriptionTokensCount,
            PurchasedTokensCount = response.PurchasedTokensCount,
        };
    }

    public async Task<ShortPaginatedResult<TokenTransactionItem>> GetTransactionsAsync(
        long organizationId,
        Guid userId,
        PaginationData pagination,
        CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationBillingInfoAsync(organizationId, cancellationToken);
        var paidEntityId = organization.Type == OrganizationType.Personal ? userId : organization.BillingId!.Value;

        var response = await client.GetTokenTransactionsAsync(
            new GetTokenTransactionsRequest
            {
                PaidEntityId = paidEntityId.ToString(),
                Page = pagination.Page,
                PerPage = pagination.PerPage,
            },
            cancellationToken: cancellationToken);

        var items = response.Items.Select(item => new TokenTransactionItem
        {
            Id = Guid.Parse(item.Id),
            Status = ToStatus(item.Status),
            Reason = ToReason(item.Reason),
            CreatedAt = item.CreatedAt.ToDateTime(),
            FinishedAt = item.FinishedAt is not null ? item.FinishedAt.ToDateTime() : null,
            Delta = item.Delta,
            Error = string.IsNullOrEmpty(item.Error) ? null : item.Error,
        }).ToList();

        return new ShortPaginatedResult<TokenTransactionItem>(
            pagination.Page, pagination.PerPage, response.HasNextPage, items);
    }

    private static TokenTransactionStatus ToStatus(ContractsTokenTransactionStatus status) => status switch
    {
        ContractsTokenTransactionStatus.Started => TokenTransactionStatus.Started,
        ContractsTokenTransactionStatus.Canceled => TokenTransactionStatus.Canceled,
        ContractsTokenTransactionStatus.Confirmed => TokenTransactionStatus.Confirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static TokenTransactionReason ToReason(ContractsTokenTransactionReason reason) => reason switch
    {
        ContractsTokenTransactionReason.TariffGrant => TokenTransactionReason.TariffGrant,
        ContractsTokenTransactionReason.DailyGrant => TokenTransactionReason.DailyGrant,
        ContractsTokenTransactionReason.Purchase => TokenTransactionReason.Purchase,
        ContractsTokenTransactionReason.Expiry => TokenTransactionReason.Expiry,
        ContractsTokenTransactionReason.Spend => TokenTransactionReason.Spend,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    private async Task<OrganizationBillingInfo> GetOrganizationBillingInfoAsync(long organizationId, CancellationToken cancellationToken)
    {
        return await context.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => new OrganizationBillingInfo(o.Type, o.BillingId))
            .SingleAsync(cancellationToken);
    }

    private readonly record struct OrganizationBillingInfo(OrganizationType Type, Guid? BillingId);

    public Task CommitTokensSpentAsync(Guid tokenTransactionId, int actualOutputTokensCount, CancellationToken cancellationToken)
    {
        return client.CommitTokensSpentAsync(
            new CommitTokensSpentRequest
            {
                TokenTransactionId = tokenTransactionId.ToString(),
                ActualOutputTokensCount = actualOutputTokensCount,
            },
            cancellationToken: cancellationToken).ResponseAsync;
    }

    public Task CancelTokensReservationAsync(Guid tokenTransactionId, string error, CancellationToken cancellationToken)
    {
        return client.CancelTokensReservationAsync(
            new CancelTokensReservationRequest
            {
                TokenTransactionId = tokenTransactionId.ToString(),
                Error = error,
            },
            cancellationToken: cancellationToken).ResponseAsync;
    }
}

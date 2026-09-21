using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Stands in for <see cref="BillingTokenClient"/> when running locally without a live Billing
/// instance (see "MockExternalServices" in <c>WebApplicationBuilderExtensions.AddCoreServices</c>).
/// Every reservation succeeds against a generous fixed balance and nothing is actually persisted
/// anywhere - there's no ledger to keep consistent since there's no real Billing database behind
/// it. Still resolves personal-vs-team from the local <see cref="Organization.Type"/> for
/// <see cref="GetOrganizationTransactionsAsync"/>'s not-supported-for-personal check, since that's
/// Boards' own data and costs nothing to keep accurate.
/// </summary>
public class FakeBillingTokenClient(DatabaseContext context) : IBillingTokenClient
{
    private const long FakeTokensPerBucket = 1_000_000;

    public Task<Guid> ReserveTokensAsync(
        long organizationId,
        Guid userId,
        int inputTokensCount,
        int maxOutputTokensCount,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Guid.NewGuid());
    }

    public Task CommitTokensSpentAsync(Guid tokenTransactionId, int actualOutputTokensCount, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task CancelTokensReservationAsync(Guid tokenTransactionId, string error, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<TokenBalance> GetBalanceAsync(long organizationId, Guid userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(new TokenBalance
        {
            FreeTokensCount = FakeTokensPerBucket,
            SubscriptionTokensCount = FakeTokensPerBucket,
            PurchasedTokensCount = 0,
        });
    }

    public Task<ShortPaginatedResult<TokenTransactionItem>> GetTransactionsAsync(
        long organizationId,
        Guid userId,
        PaginationData pagination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new ShortPaginatedResult<TokenTransactionItem>(
            pagination.Page, pagination.PerPage, false, []));
    }

    public async Task<ShortPaginatedResult<TokenTransactionItem>> GetOrganizationTransactionsAsync(
        long organizationId,
        Guid? ownerId,
        PaginationData pagination,
        CancellationToken cancellationToken)
    {
        var organizationType = await context.ActiveOrganizations()
            .Where(o => o.Id == organizationId)
            .Select(o => o.Type)
            .SingleAsync(cancellationToken);

        if (organizationType == OrganizationType.Personal)
        {
            throw new PersonalOrganizationTransactionsNotSupportedException(organizationId);
        }

        return new ShortPaginatedResult<TokenTransactionItem>(
            pagination.Page, pagination.PerPage, false, []);
    }
}

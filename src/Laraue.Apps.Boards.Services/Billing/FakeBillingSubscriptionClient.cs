using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Stands in for <see cref="BillingSubscriptionClient"/> when running locally without a live
/// Billing instance (see "MockExternalServices" in
/// <c>WebApplicationBuilderExtensions.AddCoreServices</c>). Always reports an unlimited Free-tier
/// subscription, same shape as the integration tests' default mock, so <see cref="IUsageLimitService"/>
/// never blocks a local run for lack of a real subscription.
/// </summary>
public class FakeBillingSubscriptionClient(DatabaseContext context) : IBillingSubscriptionClient
{
    private const string FakeTariffCode = "Free (local mock)";
    private const long FakeIncludedTokensCount = 2_500_000;

    public async Task<ActiveSubscriptionInfo> GetActiveSubscriptionAsync(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var organizationType = await context.ActiveOrganizations()
            .Where(o => o.Id == organizationId)
            .Select(o => o.Type)
            .SingleAsync(cancellationToken);

        return CreateUnlimitedSubscription(organizationType == OrganizationType.Personal);
    }

    public Task<ActiveSubscriptionInfo> GetActivePersonalSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(CreateUnlimitedSubscription(isPersonal: true));
    }

    public Task<string> GetTariffNameAsync(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(FakeTariffCode);
    }

    private static ActiveSubscriptionInfo CreateUnlimitedSubscription(bool isPersonal) => new()
    {
        Code = FakeTariffCode,
        IsPersonal = isPersonal,
        LimitIssuesPerMonth = null,
        LimitFreeTeamOrganizationsCount = null,
        IncludedTokensCount = FakeIncludedTokensCount,
    };
}

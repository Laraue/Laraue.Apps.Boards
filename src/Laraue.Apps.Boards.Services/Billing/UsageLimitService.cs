using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DateTime.Services.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Enforces the usage limits (issues/month, free team organizations) that come from Billing's
/// subscription rpc, alongside the permission checks in <see cref="IAccessService"/> - a shared
/// engine invoked by host-level callers (<c>WebApiServices</c>/<c>TelegramServices</c>) before a
/// mutating call to a core service, per this repo's "checks belong at the host level, not inside
/// core services" convention. Not itself a core service for that same reason.
/// </summary>
public interface IUsageLimitService
{
    /// <summary>
    /// Throws <see cref="IssueLimitExceededException"/> if <paramref name="organizationId"/> has
    /// already created <see cref="ActiveSubscriptionInfo.LimitIssuesPerMonth"/> issues in the
    /// current UTC calendar month. A null limit means unlimited - nothing to check.
    /// </summary>
    Task EnsureCanCreateIssueAsync(long organizationId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Throws <see cref="OrganizationLimitExceededException"/> if <paramref name="userId"/> already
    /// owns <see cref="ActiveSubscriptionInfo.LimitFreeTeamOrganizationsCount"/> team organizations
    /// - a limit on the user's own personal plan, checked against their personal subscription
    /// directly since the organization being created doesn't exist yet to resolve type from.
    /// </summary>
    Task EnsureCanCreateOrganizationAsync(Guid userId, CancellationToken cancellationToken);
}

public class UsageLimitService(
    DatabaseContext context,
    IBillingSubscriptionClient subscriptionClient,
    IIssueMonthlyCountService issueMonthlyCountService,
    IDateTimeProvider dateTimeProvider) : IUsageLimitService
{
    public async Task EnsureCanCreateIssueAsync(long organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionClient.GetActiveSubscriptionAsync(organizationId, userId, cancellationToken);

        if (subscription.LimitIssuesPerMonth is not { } limit)
            return;

        var now = dateTimeProvider.UtcNow;

        // A materialized counter (see IssueMonthlyCount) rather than counting Issues on every
        // check - this runs on every issue creation.
        var issuesThisMonth = await issueMonthlyCountService.GetCount(organizationId, now.Year, now.Month, cancellationToken);

        if (issuesThisMonth >= limit)
            throw new IssueLimitExceededException(limit);
    }

    public async Task EnsureCanCreateOrganizationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subscription = await subscriptionClient.GetActivePersonalSubscriptionAsync(userId, cancellationToken);

        if (subscription.LimitFreeTeamOrganizationsCount is not { } limit)
            return;

        var ownedTeamOrganizationsCount = await context.Organizations
            .Where(o => o.OwnerId == userId && o.Type == OrganizationType.Organization)
            .CountAsync(cancellationToken);

        if (ownedTeamOrganizationsCount >= limit)
            throw new OrganizationLimitExceededException(limit);
    }
}

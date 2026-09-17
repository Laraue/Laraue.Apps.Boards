using Laraue.Apps.Billing.Internal.Contracts;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Current plan/limits for an organization, from Billing's subscription rpc - same
/// personal-vs-team resolution as <see cref="IBillingTokenClient"/>. Billing auto-provisions a
/// Free subscription on first call, so there's no "no subscription" case to report here either.
/// </summary>
public interface IBillingSubscriptionClient
{
    Task<ActiveSubscriptionInfo> GetActiveSubscriptionAsync(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record ActiveSubscriptionInfo
{
    public required string Code { get; init; }

    /// <summary>
    /// Null means unlimited - Billing only sets this for tariffs that actually cap it.
    /// </summary>
    public int? LimitIssuesPerMonth { get; init; }

    /// <summary>
    /// Personal subscriptions only - always null for a team's own subscription.
    /// </summary>
    public int? LimitFreeTeamOrganizationsCount { get; init; }
}

public class BillingSubscriptionClient(
    DatabaseContext context,
    SubscriptionService.SubscriptionServiceClient client) : IBillingSubscriptionClient
{
    public async Task<ActiveSubscriptionInfo> GetActiveSubscriptionAsync(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var organization = await context.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => new { o.Type, o.BillingId })
            .SingleAsync(cancellationToken);

        var response = organization.Type == OrganizationType.Personal
            ? await client.GetActivePersonalSubscriptionAsync(
                new GetActivePersonalSubscriptionRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    UserId = userId.ToString(),
                },
                cancellationToken: cancellationToken)
            : await client.GetActiveOrganizationSubscriptionAsync(
                new GetActiveOrganizationSubscriptionRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    OrganizationId = organization.BillingId!.Value.ToString(),
                },
                cancellationToken: cancellationToken);

        return response.PayloadCase switch
        {
            ActiveSubscriptionResponse.PayloadOneofCase.LaraueBoardsPersonal => new ActiveSubscriptionInfo
            {
                Code = response.Code,
                LimitIssuesPerMonth = response.LaraueBoardsPersonal.HasLimitIssuesPerMonth
                    ? response.LaraueBoardsPersonal.LimitIssuesPerMonth
                    : null,
                LimitFreeTeamOrganizationsCount = response.LaraueBoardsPersonal.HasLimitFreeTeamOrganizationsCount
                    ? response.LaraueBoardsPersonal.LimitFreeTeamOrganizationsCount
                    : null,
            },
            ActiveSubscriptionResponse.PayloadOneofCase.LaraueBoardsTeam => new ActiveSubscriptionInfo
            {
                Code = response.Code,
                LimitIssuesPerMonth = response.LaraueBoardsTeam.HasLimitIssuesPerMonth
                    ? response.LaraueBoardsTeam.LimitIssuesPerMonth
                    : null,
            },
            _ => throw new InvalidOperationException(
                $"Unexpected subscription payload '{response.PayloadCase}' for LaraueBoards."),
        };
    }
}

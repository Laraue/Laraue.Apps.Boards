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

    /// <summary>
    /// Same as <see cref="GetActiveSubscriptionAsync"/>'s personal branch, but without resolving
    /// it from an existing organization's <c>Type</c> - needed when checking a limit tied to the
    /// user's own personal plan before an organization (the thing that would normally let us
    /// resolve personal-vs-team) exists yet, e.g. "can this user create one more team org".
    /// </summary>
    Task<ActiveSubscriptionInfo> GetActivePersonalSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Just the tariff's display name, for a caller that doesn't need its limits too - same
    /// personal-vs-team resolution as <see cref="GetActiveSubscriptionAsync"/>, but a lighter
    /// wire response.
    /// </summary>
    Task<string> GetTariffNameAsync(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken);
}

public sealed record ActiveSubscriptionInfo
{
    public required string Code { get; init; }

    /// <summary>
    /// Which of Billing's two Boards subscription shapes this came from - lets a caller build a
    /// discriminated response (e.g. a personal vs. team billing summary) rather than relying on a
    /// nullable field like <see cref="LimitFreeTeamOrganizationsCount"/> being null for two
    /// different reasons (not applicable vs. genuinely unlimited).
    /// </summary>
    public required bool IsPersonal { get; init; }

    /// <summary>
    /// Null means unlimited - Billing only sets this for tariffs that actually cap it.
    /// </summary>
    public int? LimitIssuesPerMonth { get; init; }

    /// <summary>
    /// Personal subscriptions only - always null for a team's own subscription.
    /// </summary>
    public int? LimitFreeTeamOrganizationsCount { get; init; }

    /// <summary>
    /// The tariff's own monthly token grant (not the current remaining balance - see
    /// <see cref="IBillingTokenClient.GetBalanceAsync"/> for that). Used together with
    /// <c>SubscriptionTokensCount</c> to compute how much of the plan's allowance has been spent.
    /// </summary>
    public required long IncludedTokensCount { get; init; }
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
        var organization = await GetOrganizationBillingInfoAsync(organizationId, cancellationToken);

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

        return ToInfo(response);
    }

    public async Task<ActiveSubscriptionInfo> GetActivePersonalSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var response = await client.GetActivePersonalSubscriptionAsync(
            new GetActivePersonalSubscriptionRequest
            {
                ServiceId = ServiceId.LaraueBoards,
                UserId = userId.ToString(),
            },
            cancellationToken: cancellationToken);

        return ToInfo(response);
    }

    public async Task<string> GetTariffNameAsync(
        long organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var organization = await GetOrganizationBillingInfoAsync(organizationId, cancellationToken);

        var response = organization.Type == OrganizationType.Personal
            ? await client.GetPersonalTariffNameAsync(
                new GetActivePersonalSubscriptionRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    UserId = userId.ToString(),
                },
                cancellationToken: cancellationToken)
            : await client.GetOrganizationTariffNameAsync(
                new GetActiveOrganizationSubscriptionRequest
                {
                    ServiceId = ServiceId.LaraueBoards,
                    OrganizationId = organization.BillingId!.Value.ToString(),
                },
                cancellationToken: cancellationToken);

        return response.Name;
    }

    private async Task<OrganizationBillingInfo> GetOrganizationBillingInfoAsync(
        long organizationId,
        CancellationToken cancellationToken)
    {
        return await context.ActiveOrganizations()
            .Where(o => o.Id == organizationId)
            .Select(o => new OrganizationBillingInfo(o.Type, o.BillingId))
            .SingleAsync(cancellationToken);
    }

    private readonly record struct OrganizationBillingInfo(OrganizationType Type, Guid? BillingId);

    private static ActiveSubscriptionInfo ToInfo(ActiveSubscriptionResponse response) => response.PayloadCase switch
    {
        ActiveSubscriptionResponse.PayloadOneofCase.LaraueBoardsPersonal => new ActiveSubscriptionInfo
        {
            Code = response.Code,
            IsPersonal = true,
            LimitIssuesPerMonth = response.LaraueBoardsPersonal.HasLimitIssuesPerMonth
                ? response.LaraueBoardsPersonal.LimitIssuesPerMonth
                : null,
            LimitFreeTeamOrganizationsCount = response.LaraueBoardsPersonal.HasLimitFreeTeamOrganizationsCount
                ? response.LaraueBoardsPersonal.LimitFreeTeamOrganizationsCount
                : null,
            IncludedTokensCount = response.LaraueBoardsPersonal.IncludedTokensCount,
        },
        ActiveSubscriptionResponse.PayloadOneofCase.LaraueBoardsTeam => new ActiveSubscriptionInfo
        {
            Code = response.Code,
            IsPersonal = false,
            LimitIssuesPerMonth = response.LaraueBoardsTeam.HasLimitIssuesPerMonth
                ? response.LaraueBoardsTeam.LimitIssuesPerMonth
                : null,
            IncludedTokensCount = response.LaraueBoardsTeam.IncludedTokensCount,
        },
        _ => throw new InvalidOperationException(
            $"Unexpected subscription payload '{response.PayloadCase}' for LaraueBoards."),
    };
}

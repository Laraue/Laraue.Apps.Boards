namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Thrown by <see cref="IUsageLimitService.EnsureCanCreateOrganizationAsync"/> when a user already
/// owns <see cref="Limit"/> free team organizations. Same "core exception, host-specific
/// translation" shape as <see cref="InsufficientTokenBalanceException"/>.
/// </summary>
public sealed class OrganizationLimitExceededException(int limit)
    : Exception($"Free team organization limit of {limit} reached.")
{
    public int Limit { get; } = limit;
}

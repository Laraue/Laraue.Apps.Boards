namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Thrown by <see cref="IUsageLimitService.EnsureCanCreateIssueAsync"/> when an organization has
/// already created <see cref="LimitPerMonth"/> issues in the current month. Same "core exception,
/// host-specific translation" shape as <see cref="InsufficientTokenBalanceException"/> - each host
/// catches this and translates it into its own surface's error convention.
/// </summary>
public sealed class IssueLimitExceededException(int limitPerMonth)
    : Exception($"Monthly issue limit of {limitPerMonth} reached.")
{
    public int LimitPerMonth { get; } = limitPerMonth;
}

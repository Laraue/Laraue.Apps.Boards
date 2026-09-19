namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// Thrown by <see cref="IBillingTokenClient.GetOrganizationTransactionsAsync"/> when
/// <see cref="OrganizationId"/> is a personal organization - it has no team member breakdown to
/// show (there's only one member, who already sees their own spend via
/// <see cref="IBillingTokenClient.GetTransactionsAsync"/>), so the admin ledger view doesn't
/// apply to it.
/// </summary>
public sealed class PersonalOrganizationTransactionsNotSupportedException(long organizationId)
    : Exception($"Organization {organizationId} is personal - it has no admin transaction ledger.")
{
    public long OrganizationId { get; } = organizationId;
}

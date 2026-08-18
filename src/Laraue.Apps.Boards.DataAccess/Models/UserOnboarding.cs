namespace Laraue.Apps.Boards.DataAccess.Models;

public class UserOnboarding
{
    public Guid UserId { get; set; }

    public OnboardingId OnboardingId { get; set; }

    public OnboardingStatus Status { get; set; }
    public User User { get; set; } = null!;
}

public enum OnboardingId
{
    OrganizationsV1 = 1,
    AppLayoutV1 = 2,
}

public enum OnboardingStatus
{
    Completed,
    Dismissed,
}

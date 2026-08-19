using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IUserOnboardingService
{
    Task<GetOnboardingStatusResponse> GetStatus(
        Guid userId,
        OnboardingId onboardingId,
        CancellationToken cancellationToken);

    Task SetStatus(
        Guid userId,
        OnboardingId onboardingId,
        OnboardingStatus status,
        CancellationToken cancellationToken);
}

public class UserOnboardingService(DatabaseContext context) : IUserOnboardingService
{
    public async Task<GetOnboardingStatusResponse> GetStatus(
        Guid userId,
        OnboardingId onboardingId,
        CancellationToken cancellationToken)
    {
        var status = await context.UserOnboardings
            .Where(x => x.UserId == userId && x.OnboardingId == onboardingId)
            .Select(x => (OnboardingStatus?)x.Status)
            .FirstOrDefaultAsyncEF(cancellationToken);

        return new GetOnboardingStatusResponse { Status = status };
    }

    public async Task SetStatus(
        Guid userId,
        OnboardingId onboardingId,
        OnboardingStatus status,
        CancellationToken cancellationToken)
    {
        var onboarding = new UserOnboarding
        {
            UserId = userId,
            OnboardingId = onboardingId,
            Status = status,
        };

        await context.UserOnboardings
            .Merge()
            .Using([onboarding])
            .On((target, source) =>
                target.UserId == source.UserId && target.OnboardingId == source.OnboardingId)
            .UpdateWhenMatched()
            .InsertWhenNotMatched()
            .MergeAsync(cancellationToken);
    }
}

public record SetOnboardingStatusRequest
{
    public required OnboardingStatus Status { get; init; }
}

public record GetOnboardingStatusResponse
{
    public OnboardingStatus? Status { get; init; }
}

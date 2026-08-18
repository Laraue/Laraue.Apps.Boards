using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class UserOnboardingControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<UserOnboardingController> _controller = host.Controller<UserOnboardingController>();

    [Fact]
    public async Task GetStatus_ReturnsNull_WhenStatusIsNotSet()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var response = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.GetStatus(OnboardingId.AppLayoutV1, default));

        Assert.NotNull(response);
        Assert.Null(response.Status);
    }

    [Fact]
    public async Task SetStatus_StoresStatus_WhenStatusIsNotSet()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.SetStatus(
                OnboardingId.AppLayoutV1,
                new SetOnboardingStatusRequest { Status = OnboardingStatus.Completed },
                default));

        var response = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.GetStatus(OnboardingId.AppLayoutV1, default));

        Assert.NotNull(response);
        Assert.Equal(OnboardingStatus.Completed, response.Status);
        Assert.Single(await testScope.Database.UserOnboardings.ToListAsyncEF());
    }

    [Fact]
    public async Task SetStatus_UpdatesStatus_WhenStatusAlreadyExists()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        testScope.Database.UserOnboardings.Add(new UserOnboarding
        {
            UserId = userId,
            OnboardingId = OnboardingId.AppLayoutV1,
            Status = OnboardingStatus.Completed,
        });
        await testScope.Database.SaveChangesAsync();

        await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.SetStatus(
                OnboardingId.AppLayoutV1,
                new SetOnboardingStatusRequest { Status = OnboardingStatus.Dismissed },
                default));

        var response = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.GetStatus(OnboardingId.AppLayoutV1, default));

        Assert.NotNull(response);
        Assert.Equal(OnboardingStatus.Dismissed, response.Status);
        Assert.Single(await testScope.Database.UserOnboardings.ToListAsyncEF());
    }
}

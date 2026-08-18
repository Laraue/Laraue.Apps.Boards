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
    public async Task ShouldStoreStatusByOnboardingId()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();

        var initial = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.GetStatus(OnboardingId.AppLayoutV1, default));
        Assert.Null(initial.Status);

        await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.SetStatus(
                OnboardingId.AppLayoutV1,
                new SetOnboardingStatusRequest { Status = OnboardingStatus.Completed },
                default));
        await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.SetStatus(
                OnboardingId.AppLayoutV1,
                new SetOnboardingStatusRequest { Status = OnboardingStatus.Dismissed },
                default));

        var saved = await _controller
            .WithUserAuthorization(userId)
            .Execute(x => x.GetStatus(OnboardingId.AppLayoutV1, default));
        Assert.Equal(nameof(OnboardingStatus.Dismissed), saved.Status);
        Assert.Single(await testScope.Database.UserOnboardings.ToListAsyncEF());
    }
}

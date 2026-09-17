using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.TelegramHost;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

[Collection("IntegrationTest")]
public abstract class TelegramIntegrationTest
{
    protected static AppTelegramTestHost GetTelegramTestHost()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Configuration.AddJsonFile("appsettings.json");

        builder
            .AddTelegramOptions("Telegram")
            .AddApplicationServices()
            .AddDatabaseServices("Postgre");

        var fileStorageMock = new Mock<IFileStorage>();

        builder.Services.AddSingleton(fileStorageMock.Object);

        // Registered last to override the real implementation, which would otherwise hit
        // Telegram's GetChatMember API. AdminUser/MemberUser pick the outcome per test.
        builder.Services.AddScoped<IGroupChatAdminService, FakeGroupChatAdminService>();

        // Overrides the real HTTP-backed implementation, which would otherwise hit a real AI
        // provider. Defaults to echoing the input back unchanged - /aisave tests should re-Setup
        // it (via Mock.Get on the resolved instance) for their own expectations.
        var aiContentSummarizerMock = new Mock<IAiContentSummarizer>();
        aiContentSummarizerMock.Setup(x => x.MaxOutputTokensCount).Returns(2048);
        aiContentSummarizerMock
            .Setup(x => x.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string notes, CancellationToken _) => new AiSummarizationResult(notes, InputTokensCount: 10, OutputTokensCount: 10));
        builder.Services.AddSingleton(aiContentSummarizerMock.Object);

        // Overrides the real gRPC-backed implementation, which would otherwise try to reach a
        // live Billing service. Defaults to a random successful reservation - /aisave tests that
        // care about the reserve/commit/cancel calls made should re-Setup/Verify it themselves.
        var billingTokenClientMock = new Mock<IBillingTokenClient>();
        billingTokenClientMock
            .Setup(x => x.ReserveTokensAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        builder.Services.AddSingleton(billingTokenClientMock.Object);

        // Overrides the real gRPC-backed implementation. Defaults to an unlimited subscription
        // (both limits null) so existing tests aren't tripped up by IUsageLimitService's checks -
        // tests asserting on plan/limit details should re-Setup it themselves.
        var billingSubscriptionClientMock = new Mock<IBillingSubscriptionClient>();
        var unlimitedSubscription = new ActiveSubscriptionInfo { Code = "test" };
        billingSubscriptionClientMock
            .Setup(x => x.GetActiveSubscriptionAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unlimitedSubscription);
        billingSubscriptionClientMock
            .Setup(x => x.GetActivePersonalSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unlimitedSubscription);
        builder.Services.AddSingleton(billingSubscriptionClientMock.Object);

        return new AppTelegramTestHost(builder.Services);
    }

    protected static User DefaultUser => new()
    {
        Id = 1,
        Username = "test_user",
    };

    protected static User AdminUser => new()
    {
        Id = FakeGroupChatAdminService.AdminTelegramUserId,
        Username = "admin_user",
    };

    protected static User MemberUser => new()
    {
        Id = FakeGroupChatAdminService.MemberTelegramUserId,
        Username = "member_user",
    };

    protected static Chat PrivateChat => new()
    {
        Type = ChatType.Private,
    };

    protected static Chat GroupChat => new()
    {
        Type = ChatType.Group,
    };
}
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
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
        aiContentSummarizerMock
            .Setup(x => x.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string notes, CancellationToken _) => notes);
        builder.Services.AddSingleton(aiContentSummarizerMock.Object);

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
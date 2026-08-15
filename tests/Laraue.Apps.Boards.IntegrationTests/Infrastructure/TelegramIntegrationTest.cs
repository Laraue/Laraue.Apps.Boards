using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramHost;
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
        
        return new AppTelegramTestHost(builder.Services);
    }
    
    protected static User DefaultUser => new()
    {
        Id = 1,
        Username = "test_user",
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
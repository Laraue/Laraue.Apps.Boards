using Laraue.Apps.Boards.DataAccess;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Telegram.NET.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class AppTelegramTestHost(IServiceCollection serviceCollection)
    : TelegramTestHost<Guid>(serviceCollection, CreateBotClientMock())
{
    private static ITelegramBotClient CreateBotClientMock()
    {
        var botClientMock = new Mock<ITelegramBotClient>();
        
        botClientMock.Setup(x => x.SendRequest(It.IsAny<GetFileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetFileRequest request, CancellationToken _) => new TGFile
            {
                FileId = request.FileId,
                FileUniqueId = request.FileId + "unique",
            });
        
        return botClientMock.Object;
    }
    
    protected override void BeforeFirstRequest()
    {
        TestServer.Services.UseLinq2Db();
        
        using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        dbContext.Database.Migrate();
        
        dbContext.SpaceCounters.ExecuteDelete();
        dbContext.Users.ExecuteDelete();
        dbContext.TelegramFiles.ExecuteDelete();
        dbContext.TelegramMessages.ExecuteDelete();
        dbContext.TelegramMediaGroups.ExecuteDelete();
    }

    protected override void Dispose(bool disposing)
    {
        using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        dbContext.Users.ExecuteDelete();
    }
}
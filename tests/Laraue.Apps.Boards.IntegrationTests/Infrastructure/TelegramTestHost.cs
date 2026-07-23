using Laraue.Apps.Boards.DataAccess;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Telegram.NET.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class AppTelegramTestHost(IServiceCollection serviceCollection)
    : TelegramTestHost<Guid>(serviceCollection, TelegramBotClientMockFactory.GetInstance())
{
    
    protected override void BeforeFirstRequest()
    {
        TestServer.Services.UseLinq2Db();
        
        using var scope = CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        dbContext.Database.Migrate();
        
        dbContext.CleanDatabase();
    }

    protected override void Dispose(bool disposing)
    {
    }
}
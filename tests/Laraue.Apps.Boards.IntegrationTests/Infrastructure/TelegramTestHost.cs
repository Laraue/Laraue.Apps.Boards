using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Telegram.NET.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TelegramUser = Telegram.Bot.Types.User;

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

    /// <summary>
    /// Sends a callback-query update simulating a button tap and returns the resulting
    /// message edit, so multi-step callback flows (e.g. the /link wizard) can be driven
    /// call-by-call in a test.
    /// </summary>
    public async Task<EditMessageTextRequest> SendCallbackAsync(
        TelegramUser user,
        Chat chat,
        int messageId,
        string data)
    {
        await SendUpdateAsync(new Update
        {
            CallbackQuery = new CallbackQuery
            {
                Id = Guid.NewGuid().ToString(),
                From = user,
                Message = new Message { Id = messageId, Chat = chat },
                Data = data,
            }
        });

        return Requests().Last<EditMessageTextRequest>();
    }

    public AppTelegramTestHostScope CreateTestScope()
    {
        var scope = TestServer.Services.CreateScope();
        
        return new AppTelegramTestHostScope(scope);
    }

    public class AppTelegramTestHostScope : IDisposable
    {
        private readonly IServiceScope _scope;
        private long _lastTelegramId;
        public DatabaseContext Database => _scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        public IServiceProvider Services => _scope.ServiceProvider;

        public AppTelegramTestHostScope(IServiceScope scope)
        {
            _scope = scope;
            Database.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            Database.CleanDatabase();
        }
        
        public async Task<Guid> CreateUser(Action<DataAccess.Models.User>? setupUser = null)
        {
            var user = new DataAccess.Models.User
            {
                TelegramId = ++_lastTelegramId,
            };
        
            setupUser?.Invoke(user);
        
            Database.Users.Add(user);
        
            await Database.SaveChangesAsync();
        
            return user.Id;
        }
        
        public Task<Organization> InitializeOrganization(Guid userId, Action<OrganizationInitializer>? setupInitializer = null)
        {
            var initializer = ActivatorUtilities.CreateInstance<OrganizationInitializer>(_scope.ServiceProvider, userId);
        
            initializer
                .WithName("New Org")
                .SetIsPersonal(false)
                .WithTimestamp(DateTime.UtcNow);

            setupInitializer?.Invoke(initializer);
        
            return initializer.Initialize();
        }

        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}
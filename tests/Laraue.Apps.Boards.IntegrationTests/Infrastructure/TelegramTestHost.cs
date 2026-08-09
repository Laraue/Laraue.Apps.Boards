using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
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
        
        public async Task<Guid> CreateUser(Action<User>? setupUser = null)
        {
            var user = new User
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
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiHost;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using User = Laraue.Apps.Boards.DataAccess.Models.User;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class WebApiTestHost
    : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(TelegramBotClientMockFactory.GetInstance());
        });

        return base.CreateHost(builder);
    }

    public Proxy<TController> Controller<TController>() where TController : ControllerBase
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        return new Proxy<TController>(client, this);
    }

    public WebApiTestHostScope CreateTestScope()
    {
        var scope = Services.CreateScope();
        
        return new WebApiTestHostScope(scope);
    }
}

public class WebApiTestHostScope : IDisposable
{
    private readonly IServiceScope _scope;
    public DatabaseContext Database => _scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    private long _lastTelegramId;
    
    public IServiceProvider Services => _scope.ServiceProvider;

    public WebApiTestHostScope(IServiceScope scope)
    {
        _scope = scope;
        Database.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        Database.CleanDatabase();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
    
    public async Task<Guid> CreateUser(Action<User>? setupUser = null)
    {
        var user = new User
        {
            TelegramId = ++_lastTelegramId,
        };
        
        setupUser?.Invoke(user);

        var initials = new UserInitials(user.TelegramUserName, user.TelegramFirstName, user.TelegramLastName);
        user.DisplayName = initials.DisplayName;
        user.Initials = initials.Initials;

        Database.Users.Add(user);
        
        await Database.SaveChangesAsync();
        
        return user.Id;
    }

    public Task<Organization> InitializePersonalOrganization(Guid userId, Action<OrganizationInitializer>? setupInitializer = null)
    {
        return InitializeOrganization(userId, (initializer) =>
        {
            initializer.SetIsPersonal(true);
            setupInitializer?.Invoke(initializer);
        });
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
}
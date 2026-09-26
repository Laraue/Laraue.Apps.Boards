using Grpc.Core;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.WebApiHost;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Apps.Identity.Internal.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using User = Laraue.Apps.Boards.DataAccess.Models.User;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class WebApiTestHost
    : WebApplicationFactory<Program>
{
    /// <summary>
    /// Shared for the whole test collection (see <see cref="WebApiTestHostScope"/>/IClassFixture
    /// usage) - re-<c>Setup</c> it at the start of each test rather than relying on state left by
    /// a previous test.
    /// </summary>
    public Mock<IAiContentSummarizer> AiContentSummarizerMock { get; } = new();

    /// <summary>
    /// Overrides the real gRPC-backed implementation, which would otherwise try to reach a live
    /// Billing service. Defaults to a random successful reservation - tests that care about the
    /// reserve/commit/cancel calls made should re-<c>Setup</c>/<c>Verify</c> it themselves.
    /// </summary>
    public Mock<IBillingTokenClient> BillingTokenClientMock { get; } = new();

    /// <summary>
    /// Same rationale as <see cref="BillingTokenClientMock"/> - overrides the real gRPC-backed
    /// implementation. Defaults to an unlimited subscription (both limits null) so existing tests
    /// that don't care about plan limits aren't tripped up by <see cref="IUsageLimitService"/>;
    /// tests asserting on plan/limit details should re-<c>Setup</c> it themselves.
    /// </summary>
    public Mock<IBillingSubscriptionClient> BillingSubscriptionClientMock { get; } = CreateDefaultSubscriptionClientMock();

    /// <summary>
    /// Overrides the real validator, which would check the token's signature against Google's
    /// public keys - tests can't mint a Google-signed token. No default setup: Google sign-in tests
    /// <c>Setup</c> it with the payload they need. <see cref="GoogleIdTokenValidator"/> itself is
    /// covered separately, constructed directly.
    /// </summary>
    public Mock<IGoogleIdTokenValidator> GoogleIdTokenValidatorMock { get; } = new();

    private static Mock<IBillingSubscriptionClient> CreateDefaultSubscriptionClientMock()
    {
        var mock = new Mock<IBillingSubscriptionClient>();
        var unlimited = new ActiveSubscriptionInfo { Code = "test", IsPersonal = true, IncludedTokensCount = 2_500_000 };

        mock.Setup(x => x.GetActiveSubscriptionAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unlimited);
        mock.Setup(x => x.GetActivePersonalSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unlimited);
        mock.Setup(x => x.GetTariffNameAsync(It.IsAny<long>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unlimited.Code);

        return mock;
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddJsonFile("appsettings.json", optional: true);
        });

        builder.ConfigureServices(services =>
        {
            services.AddSingleton(TelegramBotClientMockFactory.GetInstance());
            services.AddSingleton(AiContentSummarizerMock.Object);

            // Overrides the real gRPC-backed client, which would otherwise try to reach a live
            // Laraue.Apps.Identity instance. Always resolves to a fresh global id - tests that care
            // about the returned id should re-Setup it (via Mock.Get on the resolved instance).
            var identityClientMock = new Mock<UserIdentityService.UserIdentityServiceClient>();
            identityClientMock
                .Setup(x => x.CreateUserIfNotExistsAsync(
                    It.IsAny<CreateUserIfNotExistsRequest>(),
                    It.IsAny<Metadata>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CreateUserIfNotExistsRequest _, Metadata? _, DateTime? _, CancellationToken _) =>
                    GrpcTestHelpers.AsyncUnaryCallOf(new CreateUserIfNotExistsResponse { UserId = Guid.NewGuid().ToString() }));
            identityClientMock
                .Setup(x => x.CreateUserIfNotExistsByGoogleAsync(
                    It.IsAny<CreateUserIfNotExistsByGoogleRequest>(),
                    It.IsAny<Metadata>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((CreateUserIfNotExistsByGoogleRequest _, Metadata? _, DateTime? _, CancellationToken _) =>
                    GrpcTestHelpers.AsyncUnaryCallOf(new CreateUserIfNotExistsResponse { UserId = Guid.NewGuid().ToString() }));
            identityClientMock
                .Setup(x => x.LinkTelegramAccountAsync(
                    It.IsAny<LinkTelegramAccountRequest>(),
                    It.IsAny<Metadata>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((LinkTelegramAccountRequest _, Metadata? _, DateTime? _, CancellationToken _) =>
                    GrpcTestHelpers.AsyncUnaryCallOf(new LinkAccountResponse { Result = LinkAccountResult.Linked }));
            identityClientMock
                .Setup(x => x.LinkGoogleAccountAsync(
                    It.IsAny<LinkGoogleAccountRequest>(),
                    It.IsAny<Metadata>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((LinkGoogleAccountRequest _, Metadata? _, DateTime? _, CancellationToken _) =>
                    GrpcTestHelpers.AsyncUnaryCallOf(new LinkAccountResponse { Result = LinkAccountResult.Linked }));
            services.AddSingleton(identityClientMock.Object);

            services.AddSingleton(BillingTokenClientMock.Object);
            services.AddSingleton(BillingSubscriptionClientMock.Object);
            services.AddSingleton(GoogleIdTokenValidatorMock.Object);

            // Overrides the default (unnamed) IHttpClientFactory client's primary handler, so
            // CoreFilesService.GetFileContent's Telegram-download fallback (a raw, unnamed
            // httpClientFactory.CreateClient().GetByteArrayAsync(...) call - not something the
            // ITelegramBotClient mock above can intercept) returns fixed bytes instead of making
            // a real network request in tests.
            services.AddHttpClient(Options.DefaultName)
                .ConfigurePrimaryHttpMessageHandler(() => new FakeTelegramFileHttpMessageHandler());
        });

        return base.CreateHost(builder);
    }

    public Proxy<TController> Controller<TController>() where TController : ControllerBase
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        return new Proxy<TController>(client, Services);
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

        var initials = new UserInitials(user.DisplayName.Length > 0 ? user.DisplayName : null, null, null);
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
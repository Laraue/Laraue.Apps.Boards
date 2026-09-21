using Laraue.Apps.Billing.Internal.Contracts;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services.AttributeUpdaters;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.Boards.Services.Billing;
using BillingServiceId = Laraue.Apps.Billing.Internal.Contracts.ServiceId;
using BillingServiceIdInterceptor = Laraue.Apps.Billing.Internal.Contracts.ServiceIdInterceptor;
using IdentityServiceId = Laraue.Apps.Identity.Internal.Contracts.ServiceId;
using IdentityServiceIdInterceptor = Laraue.Apps.Identity.Internal.Contracts.ServiceIdInterceptor;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Laraue.Grpc.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddDatabaseServices(string connectionStringName)
        {
            var connection = GetConnection(builder, connectionStringName);
            
            builder.Services
                .AddDbContext<DatabaseContext>(opt =>
                {
                    opt
                        .UseNpgsql(connection)
                        .UseSnakeCaseNamingConvention();
                })
                .AddLinq2Db();

            return builder;
        }
        
        public WebApplicationBuilder AddCoreServices()
        {
            builder.Logging.ClearProviders();
            if (builder.Environment.IsDevelopment())
                builder.Logging.AddSimpleConsole();
            else
                builder.Logging.AddJsonConsole();

            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddScoped<IAccessService, AccessService>()
                .AddScoped<ICoreIssuesService, CoreIssuesService>()
                .AddScoped<ICoreIssueAttributesService, CoreIssueAttributesService>()
                .AddAttributeUpdaters()
                .AddScoped<IIssueHistoryService, IssueHistoryService>()
                .AddSingleton<IOrganizationLogItemFactory, OrganizationLogItemFactory>()
                .AddScoped<ICoreEpicsService, CoreEpicsService>()
                .AddScoped<ICoreStatusService, CoreStatusService>()
                .AddScoped<ICoreUserService, CoreUserService>()
                .AddScoped<ICoreSpacesService, CoreSpacesService>()
                .AddScoped<ISpaceCounterService, SpaceCounterService>()
                .AddScoped<IIssueMonthlyCountService, IssueMonthlyCountService>()
                .AddScoped<ICoreOrganizationsService, CoreOrganizationsService>()
                .AddScoped<ICoreMovementService, CoreMovementService>()
                .AddScoped<ICoreFilesService, CoreFilesService>()
                .AddScoped<IIssueNumbersService, IssueNumbersService>()
                .AddScoped<IOrganizationConcurrencyControlService, OrganizationConcurrencyControlService>()
                .AddScoped<IBillingTokenClient, BillingTokenClient>()
                .AddScoped<IBillingSubscriptionClient, BillingSubscriptionClient>()
                .AddScoped<IUsageLimitService, UsageLimitService>()
                .AddSingleton<ITokenEstimate, TokenEstimate>()
                .AddSingleton<IFileStorage, FileStorage>();

            builder.Services.AddMemoryCache();
            
            builder.Services.AddOptions<FileStorageOptions>();
            builder.Services.Configure<FileStorageOptions>(
                builder.Configuration.GetSection(nameof(FileStorageOptions)));

            builder.Services.AddOptions<AiSummarizerOptions>();
            builder.Services.Configure<AiSummarizerOptions>(
                builder.Configuration.GetSection("AiSummarizer"));

            builder.Services
                .AddHttpClient<IAiContentSummarizer, OpenAiCompatibleContentSummarizer>((sp, client) =>
                {
                    var aiOptions = sp.GetRequiredService<IOptions<AiSummarizerOptions>>().Value;
                    client.BaseAddress = new Uri(aiOptions.BaseUrl);
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        aiOptions.ApiKey);
                });

            builder.Services.AddOptions<IdentityOptions>();
            builder.Services.Configure<IdentityOptions>(
                builder.Configuration.GetSection(nameof(IdentityOptions)));

            builder.Services
                .AddGrpcClient<UserIdentityService.UserIdentityServiceClient>((sp, o) =>
                {
                    var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;
                    o.Address = new Uri(identityOptions.GrpcUrl);
                })
                .AddInterceptor(() => new IdentityServiceIdInterceptor(IdentityServiceId.LaraueBoards));

            builder.Services.AddOptions<BillingOptions>();
            builder.Services.Configure<BillingOptions>(
                builder.Configuration.GetSection("Billing"));

            // AddLaraueGrpcClient's configureClient callback has no IServiceProvider access (see
            // its signature in Laraue.Grpc.Client), so the URL is read directly off configuration
            // here rather than through IOptions<BillingOptions> like the AI client above.
            var billingOptions = builder.Configuration.GetSection("Billing").Get<BillingOptions>()
                ?? throw new InvalidOperationException("Missing 'Billing' configuration section.");

            builder.Services
                .AddLaraueGrpcClient<TokenService.TokenServiceClient>(o =>
                {
                    o.Address = new Uri(billingOptions.GrpcUrl);
                })
                .AddInterceptor(() => new BillingServiceIdInterceptor(BillingServiceId.LaraueBoards));

            // subscription.proto identifies the calling service via a request field instead of
            // the header interceptor above (see that proto's own note) - no interceptor needed.
            builder.Services
                .AddLaraueGrpcClient<SubscriptionService.SubscriptionServiceClient>(o =>
                {
                    o.Address = new Uri(billingOptions.GrpcUrl);
                });

            return builder;
        }

        private string? GetConnection(string connectionStringName)
        {
            return builder.Configuration.GetConnectionString(connectionStringName);
        }
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers one <see cref="IScalarAttributeUpdater"/> per scalar <see cref="Laraue.Apps.Boards.DataAccess.Models.AttributeType"/> -
        /// see <c>AttributeUpdaters/</c>. Singleton since they're stateless (the <c>DatabaseContext</c>
        /// they operate on is passed into <see cref="IScalarAttributeUpdater.Update"/> per call, not held).
        /// </summary>
        private IServiceCollection AddAttributeUpdaters()
        {
            return services
                .AddSingleton<IScalarAttributeUpdater, TextAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, IntegerAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, DecimalAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, DateAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, DateTimeAttributeUpdater>();
        }
    }
}

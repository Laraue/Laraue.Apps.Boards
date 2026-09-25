using Laraue.Apps.Billing.Internal.Contracts;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services.AttributeUpdaters;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.Services.Identity;
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
        /// <summary>
        /// Binds <typeparamref name="TOptions"/> to the given configuration section. In the Production
        /// environment it also validates the options' data annotations (<c>[Required]</c>, <c>[Url]</c>,
        /// ...) and <paramref name="validation"/> when the host starts, so a missing or malformed
        /// setting stops the host on deploy instead of failing on first use. Other environments
        /// (local development) only bind, so a developer isn't forced to configure every setting of
        /// every host. Use this for every options class a host binds from configuration.
        /// </summary>
        /// <param name="sectionName"></param>
        /// <param name="validation">
        /// An extra check data annotations can't express, e.g. a non-zero <see cref="long"/>.
        /// </param>
        /// <param name="failureMessage">The error reported when <paramref name="validation"/> fails.</param>
        public OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
            string sectionName,
            Func<TOptions, bool>? validation = null,
            string? failureMessage = null)
            where TOptions : class
        {
            var options = builder.Services
                .AddOptions<TOptions>()
                .Bind(builder.Configuration.GetSection(sectionName));

            if (!builder.Environment.IsProduction())
            {
                return options;
            }

            options.ValidateDataAnnotations();
            if (validation is not null)
            {
                options.Validate(validation, failureMessage ?? $"{sectionName} is invalid.");
            }

            return options.ValidateOnStart();
        }

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

            // Lets a local run substitute in-process fakes for Laraue.Apps.Identity/Billing
            // instead of the real gRPC-backed clients below, so neither service has to be running
            // locally just to exercise Boards. Defaults to false (real clients) everywhere except
            // each host's appsettings.Development.json.
            var mockExternalServices = builder.Configuration.GetValue<bool>("MockExternalServices");

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
                .AddScoped<ICoreApiKeysService, CoreApiKeysService>()
                .AddScoped<IBillingTokenClient, BillingTokenClient>()
                .AddScoped<IBillingSubscriptionClient, BillingSubscriptionClient>()
                .AddScoped<IUsageLimitService, UsageLimitService>()
                .AddSingleton<ITokenEstimate, TokenEstimate>()
                .AddSingleton<IFileStorage, FileStorage>();

            builder.Services.AddMemoryCache();
            
            builder.AddValidatedOptions<FileStorageOptions>(nameof(FileStorageOptions));

            // CoreFilesService (core, used by every Boards host) downloads/uploads files through the
            // Telegram bot, so every host needs these - not only the ones that talk to users via Telegram.
            builder.AddValidatedOptions<TelegramOptions>(
                "Telegram",
                o => o.FilesChatId != 0,
                "Telegram:FilesChatId is required.");

            builder.AddValidatedOptions<IdentityOptions>(nameof(IdentityOptions));

            builder.Services
                .AddGrpcClient<UserIdentityService.UserIdentityServiceClient>((sp, o) =>
                {
                    var identityOptions = sp.GetRequiredService<IOptions<IdentityOptions>>().Value;
                    o.Address = new Uri(identityOptions.GrpcUrl);
                })
                .AddInterceptor(() => new IdentityServiceIdInterceptor(IdentityServiceId.LaraueBoards));

            builder.AddValidatedOptions<BillingOptions>("Billing");

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

            // Local-run escape hatch: overrides the three registrations above with in-process
            // fakes (last-registered-wins, same pattern the integration tests already use to
            // override the real Telegram/AI/Billing clients) so a dev machine doesn't need
            // Laraue.Apps.Identity or Laraue.Apps.Billing actually running. The real registrations
            // above stay harmless when unused - AddGrpcClient/AddLaraueGrpcClient only open a
            // channel lazily, on the first call a real client would make.
            if (mockExternalServices)
            {
                builder.Services
                    .AddSingleton<UserIdentityService.UserIdentityServiceClient, FakeUserIdentityServiceClient>()
                    .AddScoped<IBillingTokenClient, FakeBillingTokenClient>()
                    .AddScoped<IBillingSubscriptionClient, FakeBillingSubscriptionClient>();
            }

            return builder;
        }

        /// <summary>
        /// Registers <see cref="IAiContentSummarizer"/> and its validated <see cref="AiSummarizerOptions"/>.
        /// Separate from <see cref="AddCoreServices"/> because only the hosts with AI features
        /// (WebApiHost's summarize endpoint, TelegramHost's /aisave) need it - a host that doesn't call
        /// this isn't required to configure an AI provider.
        /// </summary>
        public WebApplicationBuilder AddAiContentSummarizer()
        {
            builder.AddValidatedOptions<AiSummarizerOptions>("AiSummarizer");

            builder.Services
                .AddHttpClient<IAiContentSummarizer, OpenAiCompatibleContentSummarizer>((sp, client) =>
                {
                    var aiOptions = sp.GetRequiredService<IOptions<AiSummarizerOptions>>().Value;
                    client.BaseAddress = new Uri(aiOptions.BaseUrl);
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        aiOptions.ApiKey);
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

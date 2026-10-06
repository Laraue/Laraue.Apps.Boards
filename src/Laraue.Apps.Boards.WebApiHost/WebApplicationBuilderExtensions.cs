using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Laraue.Core.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using Telegram.Bot;
using Laraue.Apps.Boards.Services.History;

namespace Laraue.Apps.Boards.WebApiHost;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddApplicationServices()
        {
            builder
                .AddCoreServices()
                .AddAiContentSummarizer();
            builder.Services.AddHttpClient();
            builder.AddValidatedOptions<BillingOptions>("Billing");

            builder.Services
                .AddScoped<ITelegramAuthService, TelegramAuthService>()
                .AddScoped<IGoogleAuthService, GoogleAuthService>()
                .AddScoped<IConnectedAccountsService, ConnectedAccountsService>()
                .AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>()
                .AddSingleton<ITelegramBotClient, TelegramBotClient>(
                    sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.GetRequiredToken()));

            builder.Services
                .AddScoped<IIssuesService, IssuesService>()
                .AddScoped<IOrganizationHistoryService, OrganizationHistoryService>()
                .AddScoped<IOrganizationHistoryReader, OrganizationHistoryReader>()
                .AddScoped<IEpicsService, EpicsService>()
                .AddScoped<IStatusesService, StatusesService>()
                .AddScoped<IUserService, UserService>()
                .AddScoped<IUserOnboardingService, UserOnboardingService>()
                .AddScoped<ISpacesService, SpacesService>()
                .AddScoped<IOrganizationsService, OrganizationsService>()
                .AddScoped<IAdminOrganizationsService, AdminOrganizationsService>()
                .AddScoped<IBillingService, BillingService>()
                .AddScoped<IMovementService, MovementService>()
                .AddScoped<IApiKeysService, ApiKeysService>();

            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddScoped<ExceptionHandleMiddleware>();
            
            builder.Services
                .AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApi();

            return builder;
        }
        
        public WebApplicationBuilder AddAuthentication()
        {
            var stringKey = builder.Configuration["Auth:Key"] ?? throw new InvalidOperationException("Auth:Key is required.");
            var symmetricSecurityKey = AuthService.GetSymmetricSecurityKey(stringKey);

            builder.AddValidatedOptions<AuthOptions>("Auth");
            builder.AddValidatedOptions<GoogleAuthOptions>("GoogleAuth");
            
            builder.Services.AddSingleton<IAuthService, AuthService>();
            builder.Services.AddScoped<ITokenVersionService, TokenVersionService>();
            builder.Services.AddSingleton<ITokenVersionCache, MemoryTokenVersionCache>();
            builder.Services
                .AddAuthentication()
                .AddJwtBearer(AuthSchemas.User, options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = AuthService.Issuer,
                        ValidateAudience = true,
                        ValidAudience = AuthService.UserAudience,
                        IssuerSigningKey = symmetricSecurityKey,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = false,
                    };
                    ConfigureEvents(options, AuthCookies.User);
                })
                .AddJwtBearer(AuthSchemas.Organization, options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = AuthService.Issuer,
                        ValidateAudience = true,
                        ValidAudience = AuthService.OrganizationAudience,
                        IssuerSigningKey = symmetricSecurityKey,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = false,
                    };
                    ConfigureEvents(options, AuthCookies.Organization);
                });

            return builder;
        }
    }

    /// <summary>
    /// Reads the token from <paramref name="cookie"/> when there's no Authorization header, and rejects a
    /// token whose version is stale (<see cref="TokenVersionValidation"/>).
    /// </summary>
    private static void ConfigureEvents(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions options, string cookie)
    {
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (!context.Request.Headers.ContainsKey("Authorization"))
                {
                    context.Token = context.Request.Cookies[cookie];
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = TokenVersionValidation.OnTokenValidated,
        };
    }
}

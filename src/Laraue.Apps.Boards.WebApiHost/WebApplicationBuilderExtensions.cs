using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Laraue.Core.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Telegram.Bot;

namespace Laraue.Apps.Boards.WebApiHost;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddApplicationServices()
        {
            builder.AddCoreServices();
            builder.Services.AddHttpClient();

            builder.Services
                .AddScoped<ITelegramAuthService, TelegramAuthService>()
                .AddSingleton<ITelegramBotClient, TelegramBotClient>(
                    sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.Token));

            builder.Services
                .AddScoped<IIssuesService, IssuesService>()
                .AddScoped<IEpicsService, EpicsService>()
                .AddScoped<IStatusesService, StatusesService>()
                .AddScoped<IUserService, UserService>()
                .AddScoped<ISpacesService, SpacesService>()
                .AddScoped<IOrganizationsService, OrganizationsService>()
                .AddScoped<IMovementService, MovementService>();

            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddScoped<ExceptionHandleMiddleware>();
            
            builder.Services.AddControllers();
            
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApi();

            return builder;
        }
        
        public WebApplicationBuilder AddAuthentication()
        {
            var stringKey = builder.Configuration["Auth:Key"] ?? throw new InvalidOperationException("Auth:Key is required.");
            var symmetricSecurityKey = AuthService.GetSymmetricSecurityKey(stringKey);

            builder.Services.AddOptions<AuthOptions>();
            builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
            
            builder.Services.AddSingleton<IAuthService, AuthService>();
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
                    ReadTokenFromCookie(options, AuthCookies.User);
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
                    ReadTokenFromCookie(options, AuthCookies.Organization);
                });

            return builder;
        }
    }

    private static void ReadTokenFromCookie(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions options, string cookie)
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
        };
    }
}

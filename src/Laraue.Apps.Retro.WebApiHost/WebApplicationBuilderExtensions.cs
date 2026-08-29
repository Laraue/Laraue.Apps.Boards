using System.Text.Json.Serialization;
using Laraue.Apps.Boards.Auth;
using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Retro.Services;
using Laraue.Apps.Retro.WebApiServices;
using Laraue.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Laraue.Apps.Retro.WebApiHost;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddDatabaseServices(string connectionStringName)
        {
            var connection = builder.Configuration.GetConnectionString(connectionStringName);

            builder.Services.AddDbContext<DatabaseContext>(opt =>
            {
                opt
                    .UseNpgsql(connection)
                    .UseSnakeCaseNamingConvention();
            });

            return builder;
        }

        public WebApplicationBuilder AddApplicationServices()
        {
            builder.AddRetroServices();

            builder.Services.AddScoped<IRetrosService, RetrosService>();
            builder.Services.AddScoped<ExceptionHandleMiddleware>();

            builder.Services
                .AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            builder.Services
                .AddSignalR()
                .AddJsonProtocol(options => options.PayloadSerializerOptions
                    .Converters.Add(new JsonStringEnumConverter()));

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

using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.Messages;
using Laraue.Apps.Boards.TelegramServices.Services.Search;
using Laraue.Telegram.NET.Authentication.Extensions;
using Laraue.Telegram.NET.Core;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Middleware;
using Laraue.Telegram.NET.Core.Routing.Middleware;
using Laraue.Telegram.NET.Localization;
using Laraue.Telegram.NET.Localization.Extensions;
using Laraue.Telegram.NET.UpdatesQueue.EFCore.Extensions;

namespace Laraue.Apps.Boards.TelegramHost;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddTelegramOptions(string sectionName)
        {
            builder.Services.AddOptions<TelegramNetOptions>();
            builder.Services.Configure<TelegramNetOptions>(
                builder.Configuration.GetSection(sectionName));
            
            return builder;
        }
        
        public WebApplicationBuilder AddApplicationServices()
        {
            builder.AddCoreServices();
            
            builder.Services.AddOptions<AppOptions>();
            builder.Services.Configure<AppOptions>(
                builder.Configuration.GetSection(nameof(AppOptions)));
            
            builder.Services
                .AddTelegramCore()
                .AddEfCoreUpdatesQueue<DatabaseContext>()
                .AddTelegramMiddleware<HandleExceptionsMiddleware>()
                .AddTelegramMiddleware<AutoCallbackResponseMiddleware>()
                .AddTelegramMiddleware<HandlePrivateMessagesMiddleware>()
                .AddTelegramRequestLocalization<LocalizationProvider>()
                .Configure<TelegramRequestLocalizationOptions>(opt =>
                {
                    opt.AvailableLanguages = InterfaceLanguage.Available
                        .Select(x => x.Code)
                        .ToArray();
                    opt.DefaultLanguage = InterfaceLanguage.Default.Code;
                })
                .AddTelegramAuthentication<User, Guid, TelegramUserQueryService, RequestContext>();

            builder.Services
                .AddScoped<ITelegramMessageService, TelegramMessageService>()
                .AddScoped<ITelegramMessageServiceRepository, TelegramMessageServiceRepository>()
                .AddScoped<ITelegramCommandsService, TelegramCommandsService>()
                .AddScoped<ITelegramSaveMessageService, TelegramSaveMessageService>();

            builder.Services
                .AddScoped<ICoreIssuesService, CoreIssuesService>()
                .AddScoped<ICoreEpicsService, CoreEpicsService>()
                .AddScoped<ICoreStatusService, CoreStatusService>();

            builder.Services
                .AddScoped<ISearchService, SearchService>()
                .AddSingleton<ITokenFilterRegistry, TokenFilterRegistry>()
                .AddSingleton<IQueryTokenFilter, UpdatedTokenFilter>()
                .AddSingleton<IQueryTokenFilter, AssigneeTokenFilter>()
                .AddSingleton<IQueryTokenFilter, OrganizationTokenFilter>()
                .AddSingleton<IQueryTokenFilter, IssueKeyTokenFilter>()
                .AddSingleton<IQueryTokenFilter, SpaceTokenFilter>();
            
            builder.Services.AddControllers();

            return builder;
        }
    }
}
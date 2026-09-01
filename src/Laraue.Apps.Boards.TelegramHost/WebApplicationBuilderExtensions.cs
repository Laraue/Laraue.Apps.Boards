using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices;
using Laraue.Apps.Boards.TelegramServices.Services.GroupChats;
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
                .AddTelegramMiddleware<HandleGroupMessageMiddleware>()
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
                .AddScoped<ITelegramSaveMessageService, TelegramSaveMessageService>()
                .AddScoped<ISaveCommandService, SaveCommandService>()
                .AddScoped<IInfoCommandService, InfoCommandService>()
                .AddScoped<IDeleteCommandService, DeleteCommandService>()
                .AddScoped<IIssuePreviewBuilder, IssuePreviewBuilder>()
                .AddSingleton<IIssueLinkParser, IssueLinkParser>()
                .AddSingleton<IEphemeralReplySender, EphemeralReplySender>();

            builder.Services
                .AddScoped<ICoreIssuesService, CoreIssuesService>()
                .AddScoped<ICoreEpicsService, CoreEpicsService>()
                .AddScoped<ICoreStatusService, CoreStatusService>();

            builder.Services
                .AddScoped<ISearchService, SearchService>()
                .AddSingleton<IIssueUrlBuilder, IssueUrlBuilder>()
                .AddScoped<ITokenFilterRegistry, TokenFilterRegistry>()
                .AddScoped<IQueryTokenFilter, UpdatedTokenFilter>()
                .AddScoped<IQueryTokenFilter, AssigneeTokenFilter>()
                .AddScoped<IQueryTokenFilter, OrganizationTokenFilter>()
                .AddScoped<IQueryTokenFilter, IssueKeyTokenFilter>()
                .AddScoped<IQueryTokenFilter, SpaceTokenFilter>();

            builder.Services
                .AddScoped<IGroupChatService, GroupChatService>()
                .AddScoped<IGroupChatLinkService, GroupChatLinkService>()
                .AddScoped<IGroupChatAdminService, GroupChatAdminService>()
                .AddScoped<IChatMigrationService, ChatMigrationService>();
            
            builder.Services.AddControllers();

            return builder;
        }
    }
}
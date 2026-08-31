using Laraue.Apps.Boards.TelegramServices.Services.Search;
using Laraue.Apps.Boards.TelegramServices;
using Laraue.Telegram.NET.Core.Routing;
using Laraue.Telegram.NET.Core.Routing.Attributes;

namespace Laraue.Apps.Boards.TelegramHost.Controllers;

public class SearchController(ISearchService searchService) : TelegramController
{
    [TelegramInlineQueryRoute("*")]
    public Task HandleSearchRequest(
        RequestContext requestContext,
        CancellationToken ct)
    {
        return searchService.HandleInlineSearchQuery(
            new SearchRequest(
                requestContext.UserId,
                requestContext.Update.InlineQuery!.Query,
                requestContext.Update.InlineQuery.Id,
                requestContext.Update.InlineQuery.Offset),
            ct);
    }
}
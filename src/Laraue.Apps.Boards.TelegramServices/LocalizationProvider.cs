using System.Globalization;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Telegram.NET.Localization;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.TelegramServices;

public class LocalizationProvider(
    RequestContext context,
    IOptions<TelegramRequestLocalizationOptions> options,
    ILogger<BaseCultureInfoProvider> logger,
    DatabaseContext db)
    : BaseCultureInfoProvider(context, options, logger)
{
    protected override async Task<TelegramProviderCultureResult> DetermineProviderCultureResultAsync(
        CultureInfo userInterfaceCulture,
        CancellationToken cancellationToken = default)
    {
        var languageCode = await db.UserPreferences
            .Where(x => x.UserId == context.UserId)
            .Select(x => x.InterfaceLanguage)
            .FirstOrDefaultAsyncEF(cancellationToken);

        var language = InterfaceLanguage.ForCode(languageCode);

        return new TelegramProviderCultureResult(
            new CultureInfo(language.Code),
            new CultureInfo(language.Code));
    }
}
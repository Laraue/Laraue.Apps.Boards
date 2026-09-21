using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Laraue.Apps.Boards.McpHost;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        const string dbConnectionStringName = "Postgre";

        // No JWT scheme here - this host is machine-to-machine only (an MCP client authenticates
        // with a long-lived API key, never a browser session), so ApiKey is both registered and
        // the default.
        builder.Services
            .AddAuthentication(AuthSchemas.ApiKey)
            .AddApiKeyAuthentication();
        builder.Services.AddAuthorization();

        // AddCoreServices() registers ICoreFilesService, which depends on ITelegramBotClient (file
        // attachments are downloaded through Telegram regardless of which host asks for them) -
        // this host's MCP tools never touch file attachments, but the dependency still has to
        // resolve for the container to build. Same registration WebApiHost's own
        // AddApplicationServices() already does.
        builder.Services.AddOptions<TelegramOptions>();
        builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection("Telegram"));
        builder.Services.AddSingleton<ITelegramBotClient, TelegramBotClient>(
            sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.Token));

        builder
            .AddCoreServices()
            .AddDatabaseServices(dbConnectionStringName);

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = McpServerInstructions.Text)
            .WithHttpTransport()
            .WithToolsFromAssembly();

        // Tools resolve the caller's OrganizationAuthData off the current request's
        // ClaimsPrincipal (see IssueTools.GetAuthData) the same way a controller does.
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddHealthChecks();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapMcp("/mcp").RequireAuthorization();

        using (var scope = app.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await db.Database.MigrateAsync();
        }

        app.MapHealthChecks("/_health");

        await app.RunAsync();
    }
}

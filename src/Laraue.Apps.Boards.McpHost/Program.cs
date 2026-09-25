using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
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
        // attachments are downloaded through Telegram regardless of which host asks for them) - the
        // client is built from TelegramOptions, which AddCoreServices() binds and validates. Same
        // registration WebApiHost's own AddApplicationServices() already does.
        builder.Services.AddSingleton<ITelegramBotClient, TelegramBotClient>(
            sp => new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramOptions>>().Value.GetRequiredToken()));

        builder
            .AddCoreServices()
            .AddDatabaseServices(dbConnectionStringName);

        builder.Services.AddScoped<IIssueMcpService, IssueMcpService>();

        builder.Services.AddControllers();
        builder.AddValidatedOptions<ServerCardOptions>("ServerCard");

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = McpServerInstructions.Text)
            .WithHttpTransport()
            .WithToolsFromAssembly();

        // Tools resolve the caller's OrganizationAuthData off the current request's
        // ClaimsPrincipal (see IssueTools.GetAuthData) the same way a controller does.
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddHealthChecks();

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapMcp("/mcp").RequireAuthorization();
        app.MapPrometheusScrapingEndpoint("/_metrics");
        app.MapControllers();

        using (var scope = app.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await db.Database.MigrateAsync();
        }

        app.MapHealthChecks("/_health");

        await app.RunAsync();
    }
}

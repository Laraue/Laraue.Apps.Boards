using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Telegram.NET.Core.Extensions;
using Laraue.Telegram.NET.Core.Telemetry;
using Microsoft.EntityFrameworkCore;
using Laraue.Apps.Boards.Services.Metrics;
using OpenTelemetry.Metrics;

namespace Laraue.Apps.Boards.TelegramHost;

public sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        // WebApplication.CreateBuilder adds the default console providers at this line, before
        // AddCoreServices() replaces them with the JSON console. If it throws (a malformed
        // appsettings, for example), the only output is the runtime's plain-text dump, which the
        // log pipeline can't parse - see BRD-336.
        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateBuilder(args);

            const string dbConnectionStringName = "Postgre";

            builder
                .AddTelegramOptions("Telegram")
                .AddApplicationServices()
                .AddDatabaseServices(dbConnectionStringName);

            builder.Services.AddHealthChecks();

            builder.Services
                .AddOpenTelemetry()
                .WithMetrics(metrics => metrics
                    .AddMeter(BoardsMetrics.MeterName, LaraueTelegramTelemetry.SourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddPrometheusExporter());

            app = builder.Build();

            app.Services.UseLinq2Db();

            using (var scope = app.Services.CreateScope())
            {
                await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                await db.Database.MigrateAsync();

                app.MapTelegramRequests();
            }

            app.MapHealthChecks("/_health");
            app.MapPrometheusScrapingEndpoint("/_metrics");
            await app.RunAsync();
        }
        catch (Exception exception)
        {
            // An exception escaping Main makes the runtime write a plain-text dump to stderr,
            // which the JSON-parsing log pipeline reports as "log record failed to parse"
            // (BRD-336). Report it through ILogger so it lands as one JSON record, and exit
            // non-zero without rethrowing - a rethrow is what produces the dump.
            if (app is not null)
            {
                app.Logger.LogCritical(exception, "The host stopped with an unhandled error.");
            }
            else
            {
                // A failure before Build leaves no host logger; the standalone factory writes
                // the same JSON console format.
                using var loggerFactory = LoggerFactory.Create(logging => logging.AddJsonConsole());
                loggerFactory.CreateLogger("Fatal").LogCritical(exception, "The host failed to start.");
            }

            return 1;
        }

        return 0;
    }
}
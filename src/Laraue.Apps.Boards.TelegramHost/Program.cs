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
    public static async Task Main(string[] args)
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

        var app = builder.Build();

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
}
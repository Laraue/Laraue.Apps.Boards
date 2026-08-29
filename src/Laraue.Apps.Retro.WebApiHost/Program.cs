using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Retro.WebApiServices;
using Laraue.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using Scalar.AspNetCore;

namespace Laraue.Apps.Retro.WebApiHost;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        const string dbConnectionStringName = "Postgre";

        builder.Services.AddAuthorization();

        builder
            .AddAuthentication()
            .AddApplicationServices()
            .AddDatabaseServices(dbConnectionStringName);

        builder.Services.AddHealthChecks();

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        var app = builder.Build();

        var origins = builder
            .Configuration
            .GetSection("Cors:Hosts")
            .Get<string[]>();

        if (origins is not null)
        {
            app.UseCors(corsPolicyBuilder =>
                corsPolicyBuilder.WithOrigins(origins)
                    .AllowCredentials()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<RetroHub>("/hubs/retro");
        app.UseMiddleware<ExceptionHandleMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options
                    .WithTitle("Laraue Retro API")
                    .WithTheme(ScalarTheme.Purple)
                    .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Axios);
            });
        }

        using (var scope = app.Services.CreateScope())
        {
            await using var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await db.Database.MigrateAsync();
        }

        app.MapHealthChecks("/_health");
        app.MapPrometheusScrapingEndpoint("/_metrics");
        await app.RunAsync();
    }
}

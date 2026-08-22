using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services.AttributeUpdaters;
using Laraue.Core.DataAccess.Linq2DB.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using Laraue.Core.DateTime.Services.Impl;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.Services;

public static class WebApplicationBuilderExtensions
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddDatabaseServices(string connectionStringName)
        {
            var connection = GetConnection(builder, connectionStringName);
            
            builder.Services
                .AddDbContext<DatabaseContext>(opt =>
                {
                    opt
                        .UseNpgsql(connection)
                        .UseSnakeCaseNamingConvention();
                })
                .AddLinq2Db();

            return builder;
        }
        
        public WebApplicationBuilder AddCoreServices()
        {
            builder.Services
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddScoped<IAccessService, AccessService>()
                .AddScoped<ICoreIssuesService, CoreIssuesService>()
                .AddScoped<ICoreIssueAttributesService, CoreIssueAttributesService>()
                .AddAttributeUpdaters()
                .AddScoped<IIssueHistoryService, IssueHistoryService>()
                .AddSingleton<IOrganizationLogItemFactory, OrganizationLogItemFactory>()
                .AddScoped<ICoreEpicsService, CoreEpicsService>()
                .AddScoped<ICoreStatusService, CoreStatusService>()
                .AddScoped<ICoreUserService, CoreUserService>()
                .AddScoped<ICoreSpacesService, CoreSpacesService>()
                .AddScoped<ISpaceCounterService, SpaceCounterService>()
                .AddScoped<ICoreOrganizationsService, CoreOrganizationsService>()
                .AddScoped<ICoreMovementService, CoreMovementService>()
                .AddScoped<ICoreFilesService, CoreFilesService>()
                .AddScoped<IIssueNumbersService, IssueNumbersService>()
                .AddScoped<IOrganizationConcurrencyControlService, OrganizationConcurrencyControlService>()
                .AddSingleton<IFileStorage, FileStorage>();

            builder.Services.AddMemoryCache();
            
            builder.Services.AddOptions<FileStorageOptions>();
            builder.Services.Configure<FileStorageOptions>(
                builder.Configuration.GetSection(nameof(FileStorageOptions)));

            return builder;
        }

        private string? GetConnection(string connectionStringName)
        {
            return builder.Configuration.GetConnectionString(connectionStringName);
        }
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers one <see cref="IScalarAttributeUpdater"/> per scalar <see cref="Laraue.Apps.Boards.DataAccess.Models.AttributeType"/> -
        /// see <c>AttributeUpdaters/</c>. Singleton since they're stateless (the <c>DatabaseContext</c>
        /// they operate on is passed into <see cref="IScalarAttributeUpdater.Update"/> per call, not held).
        /// </summary>
        private IServiceCollection AddAttributeUpdaters()
        {
            return services
                .AddSingleton<IScalarAttributeUpdater, TextAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, IntegerAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, DecimalAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, DateAttributeUpdater>()
                .AddSingleton<IScalarAttributeUpdater, DateTimeAttributeUpdater>();
        }
    }
}
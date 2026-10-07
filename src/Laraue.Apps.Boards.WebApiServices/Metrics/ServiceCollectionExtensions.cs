using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.WebApiServices.Metrics;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the database-backed gauges of <see cref="BoardsStateMetrics"/>. For <c>WebApiHost</c> only.
        /// </summary>
        public IServiceCollection AddBoardsStateMetrics()
        {
            services.AddSingleton<BoardsStateMetrics>();
            services.AddHostedService(sp => sp.GetRequiredService<BoardsStateMetrics>());

            return services;
        }
    }
}

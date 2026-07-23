using Laraue.Apps.Boards.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class ServiceScopeExtensions
{
    public static DatabaseContext GetDatabaseContext(this IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    }
}
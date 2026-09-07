using Laraue.Apps.Boards.WebApiServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Boards-specific authorization helpers for <see cref="Proxy{TController}"/>, kept out of the
/// shared <c>Laraue.Core.Testing</c> package since they depend on this app's <see cref="IAuthService"/>.
/// </summary>
public static class ProxyAuthorizationExtensions
{
    public static Proxy<TController> WithUserAuthorization<TController>(this Proxy<TController> proxy, Guid userId)
        where TController : ControllerBase
    {
        var authService = proxy.Services.GetRequiredService<IAuthService>();
        var bearer = authService.CreateUserToken(userId);
        return proxy.WithAuthorizationToken(bearer);
    }

    public static Proxy<TController> WithOrganizationAuthorization<TController>(this Proxy<TController> proxy, long organizationId, Guid userId)
        where TController : ControllerBase
    {
        var authService = proxy.Services.GetRequiredService<IAuthService>();
        var bearer = authService.CreateOrganizationToken(organizationId, userId);
        return proxy.WithAuthorizationToken(bearer);
    }
}

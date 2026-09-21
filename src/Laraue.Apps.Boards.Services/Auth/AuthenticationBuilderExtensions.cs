using Microsoft.AspNetCore.Authentication;
using Laraue.Apps.Boards.Common;

namespace Laraue.Apps.Boards.Services.Auth;

public static class AuthenticationBuilderExtensions
{
    extension(AuthenticationBuilder builder)
    {
        /// <summary>
        /// Registers <see cref="ApiKeyAuthenticationHandler"/> under <see cref="AuthSchemas.ApiKey"/>.
        /// </summary>
        public AuthenticationBuilder AddApiKeyAuthentication()
        {
            return builder.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                AuthSchemas.ApiKey,
                _ => { });
        }
    }
}

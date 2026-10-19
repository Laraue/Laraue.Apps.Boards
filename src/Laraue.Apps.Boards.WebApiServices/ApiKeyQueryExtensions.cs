using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public static class ApiKeyQueryExtensions
{
    /// <summary>
    /// Whether the user has an unrevoked API key in the organization - what their agent runner signs in with, so
    /// without one nothing could ever pick up an issue they hand to the agent.
    /// </summary>
    public static Task<bool> HasActiveApiKey(
        this DatabaseContext context,
        OrganizationAuthData authData,
        CancellationToken cancellationToken)
    {
        return context.ApiKeys
            .AnyAsync(
                x => x.OrganizationId == authData.OrganizationId
                    && x.CreatedByUserId == authData.UserId
                    && x.RevokedAt == null,
                cancellationToken);
    }
}

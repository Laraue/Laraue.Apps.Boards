using Laraue.Apps.Boards.Services;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.WebApiServices;

public static class AccessServiceExtensions
{
    extension(IAccessService accessService)
    {
        /// <summary>
        /// Whether the user is a current member of the organization and it isn't deleted - the check before handing
        /// out an API key for it on someone's say-so (agent sign-in's Allow, OAuth's consent).
        /// </summary>
        public Task<bool> IsActiveMember(
            Guid userId,
            long organizationId,
            CancellationToken cancellationToken)
        {
            return accessService.GetOrganizations(
                userId,
                organizationUsers => organizationUsers
                    .AnyAsync(
                        x => x.OrganizationId == organizationId
                            && x.Organization!.DeletedAt == null,
                        cancellationToken));
        }
    }
}

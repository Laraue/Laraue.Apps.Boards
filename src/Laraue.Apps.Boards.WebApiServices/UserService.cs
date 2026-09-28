using Grpc.Core;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IUserService
{
    Task UpdateEpicSortOrder(
        Guid userId,
        EpicSortOrder epicSortOrder,
        CancellationToken cancellationToken = default);
    
    Task<UserDto> GetUser(
        Guid userId,
        CancellationToken cancellationToken);
}

public class UserService(
    ICoreUserService coreService,
    DatabaseContext context,
    UserIdentityService.UserIdentityServiceClient identityClient,
    ILogger<UserService> logger)
    : IUserService
{
    public Task UpdateEpicSortOrder(
        Guid userId,
        EpicSortOrder epicSortOrder,
        CancellationToken cancellationToken = default)
    {
        return coreService
            .UpdatePreferences(
                userId,
                update => update.SetProperty(p => p.EpicSortOrder, epicSortOrder),
                cancellationToken);
    }

    public async Task<UserDto> GetUser(Guid userId, CancellationToken cancellationToken)
    {
        var data = await context.ActiveUsers()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.GlobalUserId,
                User = new UserDto
                {
                    TelegramId = x.TelegramId,
                    HasGoogleAccount = x.GoogleSubject != null,
                    Palette = Palette.Colors
                },
            })
            .FirstOrThrowNotFoundEFAsync("User is not found", cancellationToken);

        var user = data.User;
        user.Initials = await GetInitialsAsync(data.GlobalUserId, cancellationToken);
        user.Preferences = await coreService.GetPreferences(userId, cancellationToken);
        user.LanguageCode = user.Preferences.InterfaceLanguage;

        return user;
    }

    /// <summary>
    /// The initials from the user's Laraue.Apps.Identity profile - the source of truth for it. Null when
    /// Identity can't be reached, so the rest of the user still loads.
    /// </summary>
    private async Task<string?> GetInitialsAsync(Guid globalUserId, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await identityClient.GetUserProfileAsync(
                new GetUserProfileRequest { UserId = globalUserId.ToString() },
                cancellationToken: cancellationToken);

            return profile.Initials;
        }
        catch (RpcException ex)
        {
            logger.LogWarning(ex, "Could not read the profile of user {GlobalUserId} from Identity", globalUserId);
            return null;
        }
    }
}

public class UserDto
{
    public long? TelegramId { get; set; }
    public bool HasGoogleAccount { get; set; }

    /// <summary>
    /// The initials from the user's global profile; null when the profile couldn't be read. How the user
    /// is shown in an organization is that organization's <c>memberProfile</c>.
    /// </summary>
    public string? Initials { get; set; }

    public string LanguageCode { get; set; } = string.Empty;
    public required string[] Palette { get; set; }
    public UserPreferencesResponse Preferences { get; set; } = null!;
}

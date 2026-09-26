using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.EFCore.Extensions;

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

public class UserService(ICoreUserService coreService, DatabaseContext context) : IUserService
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

    public Task<UserPreferencesResponse> GetPreferences(Guid userId, CancellationToken cancellationToken)
    {
        return coreService.GetPreferences(userId, cancellationToken);
    }

    public async Task<UserDto> GetUser(Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Where(x => x.Id == userId)
            .Select(x => new UserDto
            {
                DisplayName = x.DisplayName,
                Color = x.Color,
                TelegramId = x.TelegramId,
                HasGoogleAccount = x.GoogleSubject != null,
                Initials = x.Initials,
                Palette = Palette.Colors
            })
            .FirstOrThrowNotFoundEFAsync("User is not found", cancellationToken);

        user.Preferences = await coreService.GetPreferences(userId, cancellationToken);
        user.LanguageCode = user.Preferences.InterfaceLanguage;

        return user;
    }
}

public class UserDto
{
    public long? TelegramId { get; set; }
    public bool HasGoogleAccount { get; set; }
    public required string DisplayName { get; set; }
    public string LanguageCode { get; set; } = string.Empty;
    public required string Color { get; set; }
    public string? Initials { get; set; }
    public required string[] Palette { get; set; }
    public UserPreferencesResponse Preferences { get; set; } = null!;
}

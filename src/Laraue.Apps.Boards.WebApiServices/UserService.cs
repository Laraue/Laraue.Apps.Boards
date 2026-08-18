using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Core.DataAccess.EFCore.Extensions;

namespace Laraue.Apps.Boards.WebApiServices;

public interface IUserService
{
    Task<OnboardingStatus?> GetOnboardingStatus(
        Guid userId,
        OnboardingId onboardingId,
        CancellationToken cancellationToken = default);

    Task SetOnboardingStatus(
        Guid userId,
        OnboardingId onboardingId,
        OnboardingStatus status,
        CancellationToken cancellationToken = default);

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
    public Task<OnboardingStatus?> GetOnboardingStatus(
        Guid userId,
        OnboardingId onboardingId,
        CancellationToken cancellationToken = default)
    {
        return coreService.GetOnboardingStatus(userId, onboardingId, cancellationToken);
    }

    public Task SetOnboardingStatus(
        Guid userId,
        OnboardingId onboardingId,
        OnboardingStatus status,
        CancellationToken cancellationToken = default)
    {
        return coreService.SetOnboardingStatus(userId, onboardingId, status, cancellationToken);
    }

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
                Username = x.TelegramUserName,
                LanguageCode = InterfaceLanguage.ForCode(x.TelegramLanguageCode).Code,
                Color = x.Color,
                FirstName = x.TelegramFirstName,
                LastName = x.TelegramLastName,
                TelegramId = x.TelegramId,
                Palette = Palette.Colors
            })
            .FirstOrThrowNotFoundEFAsync("User is not found", cancellationToken);

        var initials = new UserInitials(
            user.Username,
            user.FirstName,
            user.LastName);

        user.Initials = initials.Initials;
        user.Preferences = await coreService.GetPreferences(userId, cancellationToken);
        
        return user;
    }
}

public class UserDto
{
    public long TelegramId { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public required string LanguageCode { get; set; }
    public required string Color { get; set; }
    public string? Initials { get; set; }
    public required string[] Palette { get; set; }
    public UserPreferencesResponse Preferences { get; set; } = new();
}

public record SetOnboardingStatusRequest
{
    public required OnboardingStatus Status { get; init; }
}

public record GetOnboardingStatusResponse
{
    public string? Status { get; init; }
}

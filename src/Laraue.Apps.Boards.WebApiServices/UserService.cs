using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;

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

    Task UpdateProfile(
        UpdateUserProfileRequest request,
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

    public async Task<UserDto> GetUser(Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.ActiveUsers()
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

    public async Task UpdateProfile(UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        var userExists = await context.ActiveUsers()
            .AnyAsync(x => x.Id == request.UserId, cancellationToken);

        if (!userExists)
            throw new NotFoundException(string.Format(ErrorMessages.EntityNotFound, "User", request.UserId));

        if (!Palette.Contains(request.Color))
            throw new BadRequestException(
                nameof(request.Color),
                string.Format(ErrorMessages.ColorNotInPalette, request.Color));

        await coreService.UpdateProfile(
            request.UserId,
            request.DisplayName.Trim(),
            request.Color,
            cancellationToken);
    }
}

public record UpdateUserProfileRequest
{
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(129)]
    public required string DisplayName { get; set; }

    [Required]
    public required string Color { get; set; }
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

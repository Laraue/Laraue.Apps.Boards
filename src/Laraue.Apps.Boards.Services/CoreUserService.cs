using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace Laraue.Apps.Boards.Services;

public interface ICoreUserService
{
    Task UpdatePreferences(
        Guid userId,
        Action<UpdateSettersBuilder<UserPreferences>> updateSetters,
        CancellationToken cancellationToken);
    
    Task<UserPreferencesResponse> GetPreferences(
        Guid userId,
        CancellationToken cancellationToken);

    Task<Guid> CreateIfTelegramIdNotExists(TelegramUserProfile profile, CancellationToken cancellationToken);
}

public class CoreUserService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    UserIdentityService.UserIdentityServiceClient identityClient) : ICoreUserService
{
    public async Task UpdatePreferences(
        Guid userId,
        Action<UpdateSettersBuilder<UserPreferences>> updateSetters,
        CancellationToken cancellationToken)
    {
        var updatedCount = await context.UserPreferences
            .Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(updateSetters, cancellationToken);
        
        if (updatedCount > 0)
            return;
        
        // The first settings setup
        var preferences = GetDefaultPreferences(userId);
        context.Add(preferences);
        
        await context.SaveChangesAsync(cancellationToken);
        await context.UserPreferences
            .Where(x => x.UserId == userId)
            .ExecuteUpdateAsync(updateSetters, cancellationToken);
    }

    public async Task<UserPreferencesResponse> GetPreferences(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var preferences = await context.UserPreferences
            .Where(x => x.UserId == userId)
            .FirstOrDefaultAsyncEF(cancellationToken)
            ?? GetDefaultPreferences(userId);

        return new UserPreferencesResponse
        {
            EpicSortOrder = preferences.EpicSortOrder,
            InterfaceLanguage = InterfaceLanguage.ForCode(preferences.InterfaceLanguage).Code,
        };
    }

    public async Task<Guid> CreateIfTelegramIdNotExists(TelegramUserProfile profile, CancellationToken cancellationToken)
    {
        var timestamp = dateTimeProvider.UtcNow;

        var initials = new UserInitials(profile.UserName, profile.FirstName, profile.LastName);
        var user = new User
        {
            Id = Guid.NewGuid(),
            TelegramId = profile.TelegramId,
            DisplayName = initials.DisplayName,
            Initials = initials.Initials,
            Color = Palette.RandomColor(),
            CreatedAt = timestamp,
            // Resolve/create the global Laraue identity for this Telegram account before touching
            // our own DB - if Laraue.Apps.Identity is unreachable, registration fails outright
            // rather than creating a Boards user with no global identity.
            GlobalUserId = await GetGlobalUserIdAsync(profile, cancellationToken),
        };

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        
        var insertedCount = await context.Users
            .Merge()
            .Using([user])
            .On((t, s) => t.TelegramId == s.TelegramId)
            .InsertWhenNotMatched()
            .MergeAsync(cancellationToken);

        if (insertedCount > 0)
        {
            var organization = OrganizationDefaults.GetNewOrganizationEntity(
                user.Id,
                OrganizationDefaults.GetPersonalOrganizationSlug(profile.UserName),
                OrganizationDefaults.GetPersonalOrganizationName(profile.LanguageCode),
                Palette.RandomColor(),
                timestamp,
                isPersonal: true);

            var defaultStatus = organization.Spaces!.Single().Epics!.Single().Statuses!.Single();

            context.Organizations.Add(organization);
            context.LinkedTelegramChats.Add(new LinkedTelegramChat
            {
                ExternalChatId = profile.TelegramId,
                Title = profile.UserName ?? profile.FirstName,
                Status = defaultStatus,
                OwnerId = user.Id,
                SaveMode = SaveMode.EachMessage,
                LinkedAt = timestamp,
            });
            context.UserPreferences.Add(GetDefaultPreferences(user.Id, profile.LanguageCode));

            await context.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return user.Id;
    }

    /// <summary>
    /// Calls Laraue.Apps.Identity to resolve (or create) this Telegram account's global user id.
    /// Lets any failure (including <see cref="Grpc.Core.RpcException"/>) propagate - a Boards user
    /// isn't created without one.
    /// </summary>
    private async Task<Guid> GetGlobalUserIdAsync(TelegramUserProfile profile, CancellationToken cancellationToken)
    {
        var request = new CreateUserIfNotExistsRequest
        {
            TelegramId = profile.TelegramId,
        };

        if (profile.UserName is { } userName) request.TelegramUsername = userName;
        if (profile.FirstName is { } firstName) request.TelegramFirstName = firstName;
        if (profile.LastName is { } lastName) request.TelegramLastName = lastName;
        if (profile.LanguageCode is { } languageCode) request.TelegramLanguageCode = languageCode;

        var response = await identityClient.CreateUserIfNotExistsAsync(request, cancellationToken: cancellationToken);

        return Guid.Parse(response.UserId);
    }

    private static UserPreferences GetDefaultPreferences(Guid userId, string? languageCode = null)
    {
        return new UserPreferences
        {
            UserId = userId,
            EpicSortOrder = EpicSortOrder.LastTouched,
            InterfaceLanguage = languageCode is null ? null : InterfaceLanguage.ForCode(languageCode).Code,
        };
    }
}

public record UserPreferencesResponse
{
    public EpicSortOrder EpicSortOrder { get; init; }

    /// <summary>
    /// Always one of <see cref="InterfaceLanguage.Available"/> - the default when not set.
    /// </summary>
    public required string InterfaceLanguage { get; init; }
}

/// <summary>
/// A Telegram user's profile as the sign-in method (Mini App, login widget, or the bot itself)
/// reported it. Used only while creating the user - forwarded to Laraue.Apps.Identity (the source of
/// truth for profiles) and used to derive the Boards-side display name, initials, personal
/// organization and interface language. Boards doesn't store it.
/// </summary>
public sealed record TelegramUserProfile(
    long TelegramId,
    string? UserName,
    string? FirstName,
    string? LastName,
    string? LanguageCode);

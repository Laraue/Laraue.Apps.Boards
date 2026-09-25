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

    /// <summary>
    /// Google counterpart of <see cref="CreateIfTelegramIdNotExists"/>: creates a Boards user (plus
    /// their personal organization and preferences) for a Google account seen for the first time,
    /// or returns the existing user's id. The caller must have verified the Google ID token already.
    /// A Google-only user has no Telegram account, so no personal Telegram chat is linked.
    /// </summary>
    Task<Guid> CreateIfGoogleSubjectNotExists(GoogleUserProfile profile, CancellationToken cancellationToken);
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

        if (insertedCount == 0)
        {
            // Lost a race with a concurrent registration of the same Telegram account.
            await transaction.CommitAsync(cancellationToken);

            return await context.Users
                .Where(x => x.TelegramId == profile.TelegramId)
                .Select(x => x.Id)
                .FirstAsyncEF(cancellationToken);
        }

        var defaultStatus = AddPersonalWorkspace(
            user.Id,
            OrganizationDefaults.GetPersonalOrganizationSlug(profile.UserName),
            profile.LanguageCode,
            timestamp);

        context.LinkedTelegramChats.Add(new LinkedTelegramChat
        {
            ExternalChatId = profile.TelegramId,
            Title = profile.UserName ?? profile.FirstName,
            Status = defaultStatus,
            OwnerId = user.Id,
            SaveMode = SaveMode.EachMessage,
            LinkedAt = timestamp,
        });

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return user.Id;
    }

    public async Task<Guid> CreateIfGoogleSubjectNotExists(GoogleUserProfile profile, CancellationToken cancellationToken)
    {
        var timestamp = dateTimeProvider.UtcNow;

        var emailLocalPart = GetEmailLocalPart(profile.Email);
        var initials = profile.GivenName is not null
            ? new UserInitials(null, profile.GivenName, profile.FamilyName)
            : new UserInitials(null, profile.Name ?? emailLocalPart, null);

        var user = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = profile.GoogleSubject,
            DisplayName = initials.DisplayName,
            Initials = initials.Initials,
            Color = Palette.RandomColor(),
            CreatedAt = timestamp,
            // Same "no Boards user without a global identity" rule as the Telegram flow.
            GlobalUserId = await GetGlobalUserIdAsync(profile, cancellationToken),
        };

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var insertedCount = await context.Users
            .Merge()
            .Using([user])
            .On((t, s) => t.GoogleSubject == s.GoogleSubject)
            .InsertWhenNotMatched()
            .MergeAsync(cancellationToken);

        if (insertedCount == 0)
        {
            // Lost a race with a concurrent registration of the same Google account.
            await transaction.CommitAsync(cancellationToken);

            return await context.Users
                .Where(x => x.GoogleSubject == profile.GoogleSubject)
                .Select(x => x.Id)
                .FirstAsyncEF(cancellationToken);
        }

        AddPersonalWorkspace(
            user.Id,
            OrganizationDefaults.GetPersonalOrganizationSlug(ToSlug(emailLocalPart)),
            profile.LanguageCode,
            timestamp);

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return user.Id;
    }

    /// <summary>
    /// Adds (without saving) what every newly registered user gets regardless of how they signed
    /// in: a personal organization and their preferences, with the interface language taken from
    /// the sign-in method. Returns the personal organization's default status.
    /// </summary>
    private DataAccess.Models.Status AddPersonalWorkspace(Guid userId, string slug, string? languageCode, DateTime timestamp)
    {
        var organization = OrganizationDefaults.GetNewOrganizationEntity(
            userId,
            slug,
            OrganizationDefaults.GetPersonalOrganizationName(languageCode),
            Palette.RandomColor(),
            timestamp,
            isPersonal: true);

        context.Organizations.Add(organization);
        context.UserPreferences.Add(GetDefaultPreferences(userId, languageCode));

        return organization.Spaces!.Single().Epics!.Single().Statuses!.Single();
    }

    private static string? GetEmailLocalPart(string? email)
    {
        var atIndex = email?.IndexOf('@') ?? -1;

        return atIndex > 0 ? email![..atIndex] : null;
    }

    /// <summary>
    /// Keeps only ASCII letters/digits, lowercased - an email local part can contain characters
    /// ('.', '+', '-') that don't belong in an organization URL slug. Null if nothing is left.
    /// </summary>
    private static string? ToSlug(string? value)
    {
        if (value is null)
            return null;

        var slug = new string(value
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .Take(32)
            .ToArray());

        return slug.Length > 0 ? slug : null;
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

    /// <summary>
    /// Google counterpart of <see cref="GetGlobalUserIdAsync(TelegramUserProfile, CancellationToken)"/>.
    /// </summary>
    private async Task<Guid> GetGlobalUserIdAsync(GoogleUserProfile profile, CancellationToken cancellationToken)
    {
        var request = new CreateUserIfNotExistsByGoogleRequest
        {
            GoogleSubject = profile.GoogleSubject,
        };

        if (profile.Email is { } email) request.Email = email;
        if (profile.Name is { } name) request.Name = name;
        if (profile.GivenName is { } givenName) request.GivenName = givenName;
        if (profile.FamilyName is { } familyName) request.FamilyName = familyName;

        var response = await identityClient.CreateUserIfNotExistsByGoogleAsync(request, cancellationToken: cancellationToken);

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

/// <summary>
/// A Google account's profile, taken from an already-verified Google ID token. Like
/// <see cref="TelegramUserProfile"/>, used only while creating the user: forwarded to
/// Laraue.Apps.Identity and used to derive the Boards-side display name, initials and personal
/// organization, but not stored. <see cref="LanguageCode"/> isn't part of the ID token - it comes
/// from the signing-in client (e.g. the browser's language).
/// </summary>
public sealed record GoogleUserProfile(
    string GoogleSubject,
    string? Email,
    string? Name,
    string? GivenName,
    string? FamilyName,
    string? LanguageCode);

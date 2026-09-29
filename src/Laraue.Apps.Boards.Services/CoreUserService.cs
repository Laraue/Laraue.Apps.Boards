using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.Services.Identity;
using Laraue.Apps.Boards.Services.Members;
using Laraue.Apps.Identity.Internal.Contracts;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public interface ICoreUserService
{
    /// <summary>
    /// First step of signing a Telegram account up: resolves (or creates) its Laraue.Apps.Identity user
    /// and the profile the user is shown by in their personal organization. Calls Identity and writes
    /// nothing to the Boards database, so call it outside a transaction; finish with
    /// <see cref="CreateIfTelegramIdNotExists"/>.
    /// </summary>
    Task<NewUserIdentity> ResolveTelegramIdentity(TelegramUserProfile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Second step, after <see cref="ResolveTelegramIdentity"/>: creates a Boards user (plus their
    /// personal organization and personal Telegram chat) for a Telegram account seen for
    /// the first time, or returns the existing user's id. Must be called within a transaction.
    /// </summary>
    Task<Guid> CreateIfTelegramIdNotExists(
        TelegramUserProfile profile,
        NewUserIdentity identity,
        CancellationToken cancellationToken);

    /// <summary>
    /// Google counterpart of <see cref="ResolveTelegramIdentity"/>. The caller must have verified the
    /// Google ID token already.
    /// </summary>
    Task<NewUserIdentity> ResolveGoogleIdentity(GoogleUserProfile profile, CancellationToken cancellationToken);

    /// <summary>
    /// Google counterpart of <see cref="CreateIfTelegramIdNotExists"/>. A Google-only user has no
    /// Telegram account, so no personal Telegram chat is linked. Must be called within a transaction.
    /// </summary>
    Task<Guid> CreateIfGoogleSubjectNotExists(
        GoogleUserProfile profile,
        NewUserIdentity identity,
        CancellationToken cancellationToken);

    /// <summary>
    /// First step of connecting a Telegram account to the existing user <paramref name="userId"/> (e.g.
    /// one who signed up with Google): checks the user doesn't have another Telegram account and that
    /// another user who has this one has no data, then links it in Laraue.Apps.Identity (moving it from
    /// that empty user there). Writes nothing to the Boards database, so call it outside a transaction;
    /// if it returns <see cref="AccountLinkOutcome.Linked"/>, finish with
    /// <see cref="LinkTelegramAccountInBoards"/>. The caller must have verified the Telegram login data.
    /// </summary>
    Task<AccountLinkOutcome> LinkTelegramAccountInIdentity(
        Guid userId,
        TelegramUserProfile profile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Second step, after <see cref="LinkTelegramAccountInIdentity"/> returned
    /// <see cref="AccountLinkOutcome.Linked"/>: moves the Telegram account to the user in Boards (taking
    /// it and its personal chat from an empty previous user, if any) and links the user's personal
    /// Telegram chat to their personal organization, as Telegram sign-up does. Safe to repeat. Must be
    /// called within a transaction.
    /// </summary>
    Task<AccountLinkInBoardsResult> LinkTelegramAccountInBoards(
        Guid userId,
        TelegramUserProfile profile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Google counterpart of <see cref="LinkTelegramAccountInIdentity"/>. The caller must have verified
    /// the Google ID token.
    /// </summary>
    Task<AccountLinkOutcome> LinkGoogleAccountInIdentity(
        Guid userId,
        GoogleUserProfile profile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Google counterpart of <see cref="LinkTelegramAccountInBoards"/>. Must be called within a transaction.
    /// </summary>
    Task<AccountLinkInBoardsResult> LinkGoogleAccountInBoards(
        Guid userId,
        GoogleUserProfile profile,
        CancellationToken cancellationToken);
}

public class CoreUserService(
    DatabaseContext context,
    IDateTimeProvider dateTimeProvider,
    UserIdentityService.UserIdentityServiceClient identityClient) : ICoreUserService
{
    public async Task<NewUserIdentity> ResolveTelegramIdentity(
        TelegramUserProfile profile,
        CancellationToken cancellationToken)
    {
        // Resolve/create the global Laraue identity before touching our own DB - if
        // Laraue.Apps.Identity is unreachable, registration fails outright rather than creating a
        // Boards user with no global identity.
        var globalUserId = await GetGlobalUserIdAsync(profile, cancellationToken);

        return new NewUserIdentity(
            globalUserId,
            await identityClient.GetNewMemberProfileAsync(globalUserId, cancellationToken));
    }

    public async Task<Guid> CreateIfTelegramIdNotExists(
        TelegramUserProfile profile,
        NewUserIdentity identity,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var timestamp = dateTimeProvider.UtcNow;

        var user = new User
        {
            Id = Guid.NewGuid(),
            TelegramId = profile.TelegramId,
            CreatedAt = timestamp,
            GlobalUserId = identity.GlobalUserId,
        };
        
        var insertedCount = await context.Users
            .Merge()
            .Using([user])
            .On((t, s) => t.TelegramId == s.TelegramId)
            .InsertWhenNotMatched()
            .MergeAsync(cancellationToken);

        if (insertedCount == 0)
        {
            // Lost a race with a concurrent registration of the same Telegram account.
            return await context.Users
                .Where(x => x.TelegramId == profile.TelegramId)
                .Select(x => x.Id)
                .FirstAsyncEF(cancellationToken);
        }

        var defaultStatus = AddPersonalWorkspace(
            user.Id,
            OrganizationDefaults.GetPersonalOrganizationSlug(profile.UserName),
            profile.LanguageCode,
            timestamp,
            identity.Profile);

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

        return user.Id;
    }

    public async Task<NewUserIdentity> ResolveGoogleIdentity(
        GoogleUserProfile profile,
        CancellationToken cancellationToken)
    {
        // Same "no Boards user without a global identity" rule as the Telegram flow.
        var globalUserId = await GetGlobalUserIdAsync(profile, cancellationToken);

        return new NewUserIdentity(
            globalUserId,
            await identityClient.GetNewMemberProfileAsync(globalUserId, cancellationToken));
    }

    public async Task<Guid> CreateIfGoogleSubjectNotExists(
        GoogleUserProfile profile,
        NewUserIdentity identity,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        var timestamp = dateTimeProvider.UtcNow;

        var emailLocalPart = GetEmailLocalPart(profile.Email);

        var user = new User
        {
            Id = Guid.NewGuid(),
            GoogleSubject = profile.GoogleSubject,
            CreatedAt = timestamp,
            GlobalUserId = identity.GlobalUserId,
        };

        var insertedCount = await context.Users
            .Merge()
            .Using([user])
            .On((t, s) => t.GoogleSubject == s.GoogleSubject)
            .InsertWhenNotMatched()
            .MergeAsync(cancellationToken);

        if (insertedCount == 0)
        {
            // Lost a race with a concurrent registration of the same Google account.
            return await context.Users
                .Where(x => x.GoogleSubject == profile.GoogleSubject)
                .Select(x => x.Id)
                .FirstAsyncEF(cancellationToken);
        }

        AddPersonalWorkspace(
            user.Id,
            OrganizationDefaults.GetPersonalOrganizationSlug(ToSlug(emailLocalPart)),
            profile.LanguageCode,
            timestamp,
            identity.Profile);

        await context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }

    public async Task<AccountLinkOutcome> LinkTelegramAccountInIdentity(
        Guid userId,
        TelegramUserProfile profile,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Where(x => x.Id == userId)
            .Select(x => new { x.GlobalUserId, x.TelegramId })
            .FirstAsyncEF(cancellationToken);

        if (user.TelegramId == profile.TelegramId)
        {
            return AccountLinkOutcome.Linked;
        }

        if (user.TelegramId is not null)
        {
            return AccountLinkOutcome.UserHasOtherAccount;
        }

        var ownerId = await context.Users
            .Where(x => x.TelegramId == profile.TelegramId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (ownerId is not null && await HasDataAsync(ownerId.Value, cancellationToken))
        {
            return AccountLinkOutcome.OwnerHasData;
        }

        var request = new LinkTelegramAccountRequest
        {
            UserId = user.GlobalUserId.ToString(),
            TelegramId = profile.TelegramId,
        };
        if (profile.UserName is { } userName) request.TelegramUsername = userName;
        if (profile.FirstName is { } firstName) request.TelegramFirstName = firstName;
        if (profile.LastName is { } lastName) request.TelegramLastName = lastName;
        if (profile.LanguageCode is { } languageCode) request.TelegramLanguageCode = languageCode;

        return ToOutcome(await identityClient.LinkTelegramAccountAsync(request, cancellationToken: cancellationToken));
    }

    public async Task<AccountLinkInBoardsResult> LinkTelegramAccountInBoards(
        Guid userId,
        TelegramUserProfile profile,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        // Serializes concurrent links of the same user or account (e.g. a double-submitted connect), so the
        // second one sees the first one's result instead of failing on the unique personal chat.
        await context.LockUsers(x => x.Id == userId || x.TelegramId == profile.TelegramId, cancellationToken);

        var previousOwner = await context.Users
            .Where(x => x.Id != userId && x.TelegramId == profile.TelegramId)
            .Select(x => new { x.Id, HasOtherAccount = x.GoogleSubject != null })
            .FirstOrDefaultAsyncEF(cancellationToken);
        Guid? mergedUserId = null;
        if (previousOwner is { HasOtherAccount: false })
        {
            await SoftDeleteMergedUserAsync(previousOwner.Id, userId, cancellationToken);
            mergedUserId = previousOwner.Id;
        }

        await context.LinkedTelegramChats
            .Where(x => x.OwnerId != userId && x.ExternalChatId == profile.TelegramId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Users
            .Where(x => x.Id != userId && x.TelegramId == profile.TelegramId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.TelegramId, (long?)null), cancellationToken);
        await context.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.TelegramId, profile.TelegramId), cancellationToken);

        var hasPersonalChat = await context.LinkedTelegramChats
            .AnyAsync(x => x.OwnerId == userId && x.ExternalChatId == profile.TelegramId, cancellationToken);
        var personalStatusId = await GetPersonalOrganizationDefaultStatusIdAsync(userId, cancellationToken);
        if (hasPersonalChat || personalStatusId is null)
        {
            return new AccountLinkInBoardsResult(mergedUserId);
        }

        context.LinkedTelegramChats.Add(new LinkedTelegramChat
        {
            ExternalChatId = profile.TelegramId,
            Title = profile.UserName ?? profile.FirstName,
            StatusId = personalStatusId.Value,
            OwnerId = userId,
            SaveMode = SaveMode.EachMessage,
            LinkedAt = dateTimeProvider.UtcNow,
        });
        await context.SaveChangesAsync(cancellationToken);

        return new AccountLinkInBoardsResult(mergedUserId);
    }

    public async Task<AccountLinkOutcome> LinkGoogleAccountInIdentity(
        Guid userId,
        GoogleUserProfile profile,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .Where(x => x.Id == userId)
            .Select(x => new { x.GlobalUserId, x.GoogleSubject })
            .FirstAsyncEF(cancellationToken);

        if (user.GoogleSubject == profile.GoogleSubject)
        {
            return AccountLinkOutcome.Linked;
        }

        if (user.GoogleSubject is not null)
        {
            return AccountLinkOutcome.UserHasOtherAccount;
        }

        var ownerId = await context.Users
            .Where(x => x.GoogleSubject == profile.GoogleSubject)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsyncEF(cancellationToken);

        if (ownerId is not null && await HasDataAsync(ownerId.Value, cancellationToken))
        {
            return AccountLinkOutcome.OwnerHasData;
        }

        var request = new LinkGoogleAccountRequest
        {
            UserId = user.GlobalUserId.ToString(),
            GoogleSubject = profile.GoogleSubject,
        };
        if (profile.Email is { } email) request.Email = email;
        if (profile.Name is { } name) request.Name = name;
        if (profile.GivenName is { } givenName) request.GivenName = givenName;
        if (profile.FamilyName is { } familyName) request.FamilyName = familyName;

        return ToOutcome(await identityClient.LinkGoogleAccountAsync(request, cancellationToken: cancellationToken));
    }

    public async Task<AccountLinkInBoardsResult> LinkGoogleAccountInBoards(
        Guid userId,
        GoogleUserProfile profile,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        await context.LockUsers(x => x.Id == userId || x.GoogleSubject == profile.GoogleSubject, cancellationToken);

        var previousOwner = await context.Users
            .Where(x => x.Id != userId && x.GoogleSubject == profile.GoogleSubject)
            .Select(x => new { x.Id, HasOtherAccount = x.TelegramId != null })
            .FirstOrDefaultAsyncEF(cancellationToken);
        Guid? mergedUserId = null;
        if (previousOwner is { HasOtherAccount: false })
        {
            await SoftDeleteMergedUserAsync(previousOwner.Id, userId, cancellationToken);
            mergedUserId = previousOwner.Id;
        }

        await context.Users
            .Where(x => x.Id != userId && x.GoogleSubject == profile.GoogleSubject)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.GoogleSubject, (string?)null), cancellationToken);
        await context.Users
            .Where(x => x.Id == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.GoogleSubject, profile.GoogleSubject), cancellationToken);

        return new AccountLinkInBoardsResult(mergedUserId);
    }

    /// <summary>
    /// Maps Identity's link result. <c>MOVED</c> counts as linked: Boards already checked the previous
    /// owner has no data before calling (see <see cref="HasDataAsync"/>).
    /// </summary>
    private static AccountLinkOutcome ToOutcome(LinkAccountResponse response)
    {
        return response.Result switch
        {
            LinkAccountResult.Linked or LinkAccountResult.Moved => AccountLinkOutcome.Linked,
            LinkAccountResult.OwnerUsedByAnotherService => AccountLinkOutcome.OwnerUsedByAnotherService,
            LinkAccountResult.UserHasOtherAccount => AccountLinkOutcome.UserHasOtherAccount,
            _ => throw new InvalidOperationException($"Unexpected Identity link result '{response.Result}'."),
        };
    }

    /// <summary>
    /// Whether the user has done anything in Boards - if not, one of their sign-in accounts can be moved
    /// to another user without losing anything. What sign-up created doesn't count: their personal
    /// Telegram chat (the chat whose id is their own Telegram id) and a personal organization that still has only
    /// its default space, default board and single status, no attributes and no other members.
    /// Organization history only records issues and comments, so changes to that structure are
    /// checked directly.
    /// </summary>
    private async Task<bool> HasDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        var telegramId = await context.Users
            .Where(x => x.Id == userId)
            .Select(x => x.TelegramId)
            .FirstAsyncEF(cancellationToken);

        return await context.Issues.AnyAsync(x => x.OwnerId == userId || x.AssigneeId == userId, cancellationToken)
            || await context.IssueComments.AnyAsync(x => x.OwnerId == userId, cancellationToken)
            || await context.OrganizationLogs.AnyAsync(x => x.OwnerId == userId, cancellationToken)
            || await context.OrganizationUsers.AnyAsync(
                x => x.UserId == userId && x.Organization!.OwnerId != userId, cancellationToken)
            || await context.Organizations.AnyAsync(
                x => x.OwnerId == userId && x.Type != OrganizationType.Personal, cancellationToken)
            || await context.Spaces.AnyAsync(
                x => x.Organization!.OwnerId == userId && !x.IsDefault, cancellationToken)
            || await context.Epics.AnyAsync(
                x => x.Space!.Organization!.OwnerId == userId && !x.IsDefault, cancellationToken)
            || await context.Statuses.CountAsync(
                x => x.Epic!.Space!.Organization!.OwnerId == userId, cancellationToken) > 1
            || await context.Attributes.AnyAsync(x => x.Organization!.OwnerId == userId, cancellationToken)
            || await context.OrganizationUsers.AnyAsync(
                x => x.Organization!.OwnerId == userId && x.UserId != userId, cancellationToken)
            || await context.ApiKeys.AnyAsync(x => x.CreatedByUserId == userId, cancellationToken)
            || await context.Retros.AnyAsync(x => x.OwnerId == userId, cancellationToken)
            || await context.RetroParticipants.AnyAsync(x => x.UserId == userId, cancellationToken)
            || await context.RetroCards.AnyAsync(x => x.AuthorId == userId, cancellationToken)
            || await context.RetroCardVotes.AnyAsync(x => x.UserId == userId, cancellationToken)
            || await context.LinkedTelegramChats.AnyAsync(
                x => x.OwnerId == userId && x.ExternalChatId != telegramId, cancellationToken);
    }

    /// <summary>
    /// The previous owner is losing their last sign-in account to <paramref name="userId"/>, so nobody
    /// can use their account any more: soft-deletes the user, with <paramref name="userId"/> as the deleter.
    /// Their personal organization stays as is - nobody else is a member of it (see <see cref="HasDataAsync"/>),
    /// so it can't be reached any more. Laraue.Apps.Identity records which user they were absorbed into.
    /// Bumps their <see cref="User.TokenVersion"/>, ending the sessions still signed in as them (BRD-222).
    /// </summary>
    private async Task SoftDeleteMergedUserAsync(Guid previousOwnerId, Guid userId, CancellationToken cancellationToken)
    {
        await context.Users
            .Where(x => x.Id == previousOwnerId)
            .ExecuteUpdateAsync(x => x
                .SetProperty(u => u.DeletedAt, dateTimeProvider.UtcNow)
                .SetProperty(u => u.DeletedByUserId, userId)
                .SetProperty(u => u.TokenVersion, u => u.TokenVersion + 1),
                cancellationToken);
    }

    private Task<long?> GetPersonalOrganizationDefaultStatusIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return context.ActiveStatuses()
            .Where(x => x.Epic!.IsDefault
                && x.Epic.Space!.IsDefault
                && x.Epic.Space.Organization!.OwnerId == userId
                && x.Epic.Space.Organization.Type == OrganizationType.Personal
                && x.Epic.Space.Organization.DeletedAt == null)
            .OrderBy(x => x.SortOrder)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsyncEF(cancellationToken);
    }

    /// <summary>
    /// Adds (without saving) what every newly registered user gets regardless of how they signed
    /// in: a personal organization, named in the sign-in method's language. Returns the personal
    /// organization's default status.
    /// </summary>
    private DataAccess.Models.Status AddPersonalWorkspace(
        Guid userId,
        string slug,
        string? languageCode,
        DateTime timestamp,
        MemberProfile ownerProfile)
    {
        var organization = OrganizationDefaults.GetNewOrganizationEntity(
            userId,
            slug,
            OrganizationDefaults.GetPersonalOrganizationName(languageCode),
            Palette.RandomColor(),
            timestamp,
            isPersonal: true,
            ownerProfile);

        context.Organizations.Add(organization);

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
}

/// <summary>
/// A signing-up user's Laraue.Apps.Identity id and the profile they're shown by in their personal
/// organization - resolved outside the database transaction the user is then created in.
/// </summary>
public sealed record NewUserIdentity(Guid GlobalUserId, MemberProfile Profile);

/// <summary>
/// Result of a <c>Link…AccountInBoards</c> step. <paramref name="MergedUserId"/> is the previous owner
/// soft-deleted because the account was their last sign-in method, or null - their token version was
/// bumped, so a caller caching it should drop the cached value once the transaction commits.
/// </summary>
public sealed record AccountLinkInBoardsResult(Guid? MergedUserId);

/// <summary>
/// A Telegram user's profile as the sign-in method (Mini App, login widget, or the bot itself)
/// reported it. Used only while creating the user - forwarded to Laraue.Apps.Identity (the source of
/// truth for profiles) and used to derive the Boards-side display name, initials and personal
/// organization. Boards doesn't store it.
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

public enum AccountLinkOutcome
{
    /// <summary>The account is now connected to the user (it may have been already).</summary>
    Linked,

    /// <summary>The user already has a different account of this kind - one Telegram, one Google per user.</summary>
    UserHasOtherAccount,

    /// <summary>Another Boards user already has this account and has data, so it wasn't moved.</summary>
    OwnerHasData,

    /// <summary>
    /// Another Laraue app uses the account's current owner, so Identity didn't move it - moving it
    /// would lose that app's user.
    /// </summary>
    OwnerUsedByAnotherService,
}

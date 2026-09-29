using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.WebApiServices;

public sealed class ConnectGoogleAccountRequest
{
    /// <summary>
    /// The ID token (credential) the frontend got from Google's sign-in button. Pass either this
    /// or <see cref="Code"/>.
    /// </summary>
    public string? IdToken { get; init; }

    /// <summary>
    /// The authorization code the frontend got from Google's OAuth popup. Pass either this or
    /// <see cref="IdToken"/>.
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>
/// Result of connecting a sign-in account. Refusals are ordinary outcomes rather than HTTP errors, so
/// the frontend can show a specific message for each; an invalid Telegram signature or Google token
/// is still a 403.
/// </summary>
public sealed class ConnectAccountResponse
{
    public required AccountLinkOutcome Outcome { get; init; }
}

public interface IConnectedAccountsService
{
    /// <summary>
    /// Connects a Telegram account to the user. <paramref name="verifiedProfile"/> must come from
    /// Telegram login data whose signature the caller has already checked.
    /// </summary>
    Task<ConnectAccountResponse> ConnectTelegram(
        Guid userId,
        TelegramUserProfile verifiedProfile,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the Google ID token or authorization code and connects that Google account to the user.
    /// </summary>
    Task<ConnectAccountResponse> ConnectGoogle(
        Guid userId,
        ConnectGoogleAccountRequest request,
        CancellationToken cancellationToken);
}

public class ConnectedAccountsService(
    DatabaseContext context,
    ICoreUserService coreUserService,
    IGoogleIdTokenValidator googleIdTokenValidator,
    ITokenVersionService tokenVersionService)
    : IConnectedAccountsService
{
    public async Task<ConnectAccountResponse> ConnectTelegram(
        Guid userId,
        TelegramUserProfile verifiedProfile,
        CancellationToken cancellationToken)
    {
        var outcome = await coreUserService.LinkTelegramAccountInIdentity(userId, verifiedProfile, cancellationToken);
        if (outcome == AccountLinkOutcome.Linked)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var result = await coreUserService.LinkTelegramAccountInBoards(userId, verifiedProfile, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            ForgetMergedUser(result);
        }

        return new ConnectAccountResponse { Outcome = outcome };
    }

    public async Task<ConnectAccountResponse> ConnectGoogle(
        Guid userId,
        ConnectGoogleAccountRequest request,
        CancellationToken cancellationToken)
    {
        var payload = await googleIdTokenValidator.ValidateAsync(request.IdToken, request.Code, cancellationToken);
        var profile = new GoogleUserProfile(
            payload.Subject,
            payload.Email,
            payload.Name,
            payload.GivenName,
            payload.FamilyName,
            LanguageCode: null);

        var outcome = await coreUserService.LinkGoogleAccountInIdentity(userId, profile, cancellationToken);
        if (outcome == AccountLinkOutcome.Linked)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var result = await coreUserService.LinkGoogleAccountInBoards(userId, profile, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            ForgetMergedUser(result);
        }

        return new ConnectAccountResponse { Outcome = outcome };
    }

    /// <summary>
    /// The merged user's sessions end at once on this host, instead of when its cached token version expires.
    /// </summary>
    private void ForgetMergedUser(AccountLinkInBoardsResult result)
    {
        if (result.MergedUserId is { } mergedUserId)
            tokenVersionService.Forget(mergedUserId);
    }
}

using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;

namespace Laraue.Apps.Boards.WebApiServices;

public sealed class ConnectGoogleAccountRequest
{
    /// <summary>
    /// The ID token (credential) the frontend got from Google's sign-in button.
    /// </summary>
    public required string IdToken { get; init; }
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
    /// Verifies the Google ID token and connects that Google account to the user.
    /// </summary>
    Task<ConnectAccountResponse> ConnectGoogle(
        Guid userId,
        ConnectGoogleAccountRequest request,
        CancellationToken cancellationToken);
}

public class ConnectedAccountsService(
    DatabaseContext context,
    ICoreUserService coreUserService,
    IGoogleIdTokenValidator googleIdTokenValidator)
    : IConnectedAccountsService
{
    public async Task<ConnectAccountResponse> ConnectTelegram(
        Guid userId,
        TelegramUserProfile verifiedProfile,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var outcome = await coreUserService.LinkTelegramAccount(userId, verifiedProfile, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ConnectAccountResponse { Outcome = outcome };
    }

    public async Task<ConnectAccountResponse> ConnectGoogle(
        Guid userId,
        ConnectGoogleAccountRequest request,
        CancellationToken cancellationToken)
    {
        var payload = await googleIdTokenValidator.ValidateAsync(request.IdToken, cancellationToken);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var outcome = await coreUserService.LinkGoogleAccount(
            userId,
            new GoogleUserProfile(
                payload.Subject,
                payload.Email,
                payload.Name,
                payload.GivenName,
                payload.FamilyName,
                LanguageCode: null),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ConnectAccountResponse { Outcome = outcome };
    }
}

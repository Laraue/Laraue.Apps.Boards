using Google.Apis.Auth;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.WebApiServices;

public class GoogleAuthOptions
{
    /// <summary>
    /// OAuth client id of the web app in Google Cloud Console. An ID token is accepted only if it
    /// was issued for this client (its <c>aud</c> claim).
    /// </summary>
    public required string ClientId { get; set; }
}

public sealed class GoogleAuthRequest
{
    /// <summary>
    /// The ID token (credential) the frontend got from Google's sign-in button.
    /// </summary>
    public required string IdToken { get; init; }

    /// <summary>
    /// The browser's language, used as the interface language of a newly registered user. The ID
    /// token itself carries no language.
    /// </summary>
    public string? LanguageCode { get; init; }
}

/// <summary>
/// Claims of a Google ID token that passed validation.
/// </summary>
public sealed record GoogleIdTokenPayload(
    string Subject,
    string? Email,
    string? Name,
    string? GivenName,
    string? FamilyName);

public interface IGoogleIdTokenValidator
{
    /// <summary>
    /// Verifies the token's signature, expiry, issuer and that it was issued for
    /// <see cref="GoogleAuthOptions.ClientId"/>. Throws <see cref="ForbiddenException"/> if any
    /// check fails.
    /// </summary>
    Task<GoogleIdTokenPayload> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

public class GoogleIdTokenValidator(IOptions<GoogleAuthOptions> options) : IGoogleIdTokenValidator
{
    public async Task<GoogleIdTokenPayload> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var clientId = options.Value.ClientId;

        // An empty audience list makes the verification skip the audience check entirely, i.e.
        // accept a token issued for *any* Google OAuth client - refuse to run like that.
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("GoogleAuth:ClientId is not configured.");

        GoogleJsonWebSignature.Payload payload;

        try
        {
            // The lower-level equivalent of GoogleJsonWebSignature.ValidateAsync, which has no
            // CancellationToken overload. CertificatesUrl is left unset, so Google's signing keys
            // are used; the issuers must be listed explicitly, since a null TrustedIssuers skips
            // the issuer check.
            payload = await JsonWebSignature.VerifySignedTokenAsync<GoogleJsonWebSignature.Payload>(
                idToken,
                new SignedTokenVerificationOptions
                {
                    TrustedAudiences = { clientId },
                    TrustedIssuers = { "accounts.google.com", "https://accounts.google.com" },
                },
                cancellationToken);
        }
        catch (InvalidJwtException)
        {
            throw new ForbiddenException(ErrorMessages.GoogleIdTokenInvalid);
        }

        return new GoogleIdTokenPayload(
            payload.Subject,
            payload.Email,
            payload.Name,
            payload.GivenName,
            payload.FamilyName);
    }
}

public interface IGoogleAuthService
{
    /// <summary>
    /// Validates the Google ID token, registers the user on their first sign-in, and returns a
    /// user JWT - the Google counterpart of Telegram's Mini App / login widget authentication.
    /// </summary>
    Task<string> Authenticate(GoogleAuthRequest request, CancellationToken cancellationToken);
}

public class GoogleAuthService(
    IGoogleIdTokenValidator tokenValidator,
    DatabaseContext context,
    ICoreUserService coreUserService,
    IAuthService authService)
    : IGoogleAuthService
{
    public async Task<string> Authenticate(GoogleAuthRequest request, CancellationToken cancellationToken)
    {
        var payload = await tokenValidator.ValidateAsync(request.IdToken, cancellationToken);

        var existingUserId = await context.Users
            .Where(x => x.GoogleSubject == payload.Subject)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var userId = existingUserId ?? await coreUserService.CreateIfGoogleSubjectNotExists(
            new GoogleUserProfile(
                payload.Subject,
                payload.Email,
                payload.Name,
                payload.GivenName,
                payload.FamilyName,
                request.LanguageCode),
            cancellationToken);

        return authService.CreateUserToken(userId);
    }
}

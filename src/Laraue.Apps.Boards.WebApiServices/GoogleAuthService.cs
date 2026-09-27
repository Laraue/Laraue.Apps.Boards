using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.WebApiServices.Resources;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.WebApiServices;

public class GoogleAuthOptions
{
    /// <summary>
    /// OAuth client id of the web app in Google Cloud Console. An ID token is accepted only if it
    /// was issued for this client (its <c>aud</c> claim).
    /// </summary>
    [Required]
    public required string ClientId { get; set; }

    /// <summary>
    /// OAuth client secret of the same client. Needed only to exchange an authorization code from
    /// the frontend's own "Sign in with Google" button for an ID token.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
}

public sealed class GoogleAuthRequest
{
    /// <summary>
    /// The ID token (credential) the frontend got from Google's sign-in button. Pass either this
    /// or <see cref="Code"/>.
    /// </summary>
    public string? IdToken { get; init; }

    /// <summary>
    /// The authorization code the frontend got from Google's OAuth popup (code client with the
    /// <c>postmessage</c> redirect). Pass either this or <see cref="IdToken"/>.
    /// </summary>
    public string? Code { get; init; }

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

    /// <summary>
    /// Exchanges an authorization code from the frontend's OAuth popup for an ID token and
    /// validates it like <see cref="ValidateAsync"/>. Throws <see cref="ForbiddenException"/> if
    /// the code is invalid, expired or already used.
    /// </summary>
    Task<GoogleIdTokenPayload> ExchangeCodeAsync(string code, CancellationToken cancellationToken);
}

public static class GoogleIdTokenValidatorExtensions
{
    /// <summary>
    /// Validates whichever Google credential the frontend sent - an authorization code or an ID
    /// token.
    /// </summary>
    public static Task<GoogleIdTokenPayload> ValidateAsync(
        this IGoogleIdTokenValidator validator,
        string? idToken,
        string? code,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(code))
            return validator.ExchangeCodeAsync(code, cancellationToken);
        if (!string.IsNullOrEmpty(idToken))
            return validator.ValidateAsync(idToken, cancellationToken);

        throw new BadRequestException("code", ErrorMessages.GoogleCredentialMissing);
    }
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

    public async Task<GoogleIdTokenPayload> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        var (clientId, clientSecret) = (options.Value.ClientId, options.Value.ClientSecret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("GoogleAuth:ClientId and GoogleAuth:ClientSecret must be configured.");

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
        });

        TokenResponse response;
        try
        {
            // "postmessage" is the redirect URI of Google's JS code client in popup mode.
            response = await flow.ExchangeCodeForTokenAsync(
                userId: string.Empty,
                code,
                redirectUri: "postmessage",
                cancellationToken);
        }
        catch (TokenResponseException)
        {
            throw new ForbiddenException(ErrorMessages.GoogleIdTokenInvalid);
        }

        if (string.IsNullOrEmpty(response.IdToken))
            throw new ForbiddenException(ErrorMessages.GoogleIdTokenInvalid);

        return await ValidateAsync(response.IdToken, cancellationToken);
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
        var payload = await tokenValidator.ValidateAsync(request.IdToken, request.Code, cancellationToken);

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

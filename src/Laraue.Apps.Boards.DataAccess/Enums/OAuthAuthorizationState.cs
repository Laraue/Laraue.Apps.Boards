namespace Laraue.Apps.Boards.DataAccess.Enums;

/// <summary>
/// Where an <see cref="Models.OAuthAuthorizationRequest"/> is. Expiry isn't a state - a request past its
/// <see cref="Models.OAuthAuthorizationRequest.ExpiresAt"/> is expired whatever its state. The values are stored in
/// the database, so an existing value never changes its number.
/// </summary>
public enum OAuthAuthorizationState
{
    /// <summary>
    /// The client sent the person to the consent page; the person hasn't answered yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The person allowed it for an organization and the client got its authorization code, which it hasn't
    /// exchanged for a token yet.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// The person refused it.
    /// </summary>
    Denied = 2,

    /// <summary>
    /// The client exchanged the code for its token (an API key) - see
    /// <see cref="Models.OAuthAuthorizationRequest.ApiKeyId"/>. Final: a code is redeemed once.
    /// </summary>
    Completed = 3,
}

namespace Laraue.Apps.Boards.DataAccess.Enums;

/// <summary>
/// Where an <see cref="Models.AgentSignInRequest"/> is. Expiry isn't a state - a request past its
/// <see cref="Models.AgentSignInRequest.ExpiresAt"/> is expired whatever its state. The values are stored in the
/// database, so an existing value never changes its number.
/// </summary>
public enum AgentSignInState
{
    /// <summary>
    /// The runner started the sign-in; the person hasn't answered on the Allow page yet.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The person allowed it for an organization; the runner hasn't picked up its key yet.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// The person refused it.
    /// </summary>
    Denied = 2,

    /// <summary>
    /// The runner received its API key - see <see cref="Models.AgentSignInRequest.ApiKeyId"/>. Final: a key is
    /// handed out once.
    /// </summary>
    Completed = 3,
}

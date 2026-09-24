namespace Laraue.Apps.Boards.Common;

public struct OrganizationAuthData
{
    public long OrganizationId { get; init; }
    public Guid UserId { get; init; }

    /// <summary>
    /// The API key the caller authenticated with, or null for a normal JWT-authenticated request
    /// (the web app, Telegram). Only ever set when the request came through an
    /// API-key-authenticating host (currently just the MCP host).
    /// </summary>
    public Guid? ApiKeyId { get; init; }

    /// <summary>
    /// Bundles <see cref="UserId"/>/<see cref="ApiKeyId"/> into an <see cref="Actor"/>, for
    /// passing to a mutation that needs to attribute the change on history.
    /// </summary>
    public Actor ToActor() => new(UserId, ApiKeyId);
}

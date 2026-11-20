using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// One OAuth authorization of an MCP client such as Claude or ChatGPT (BRD-362): authorization code with PKCE, the
/// client identified by its Client ID Metadata Document. The client sends the person to the authorize endpoint, the
/// person allows or denies it on the consent page for an organization, and the client exchanges the code for its
/// token - an <see cref="ApiKey"/> created for that person and organization, returned once and never stored raw.
/// Like <see cref="AgentSignInRequest"/>, a way to hand out an organization-scoped Boards key; a general sign-in
/// server still belongs to Laraue.Apps.Identity.
/// </summary>
public class OAuthAuthorizationRequest
{
    /// <summary>
    /// Also how the consent page finds the request.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The client's <c>client_id</c>: the HTTPS URL of its metadata document, e.g.
    /// <c>https://chatgpt.com/oauth/client.json</c>.
    /// </summary>
    [MaxLength(Constraints.MaxOAuthUrlLength)]
    public required string ClientId { get; set; }

    /// <summary>
    /// The client's <c>client_name</c> from its metadata document, shown on the consent page and put in the key's name.
    /// </summary>
    [MaxLength(Constraints.MaxOAuthClientNameLength)]
    public required string ClientName { get; set; }

    /// <summary>
    /// Where the browser goes back to with the code - one of the document's <c>redirect_uris</c>. The token request
    /// must send the same one.
    /// </summary>
    [MaxLength(Constraints.MaxOAuthUrlLength)]
    public required string RedirectUri { get; set; }

    /// <summary>
    /// The client's <c>state</c>, sent back to it unchanged with the code or the error.
    /// </summary>
    [MaxLength(Constraints.MaxOAuthStateLength)]
    public string? ClientState { get; set; }

    /// <summary>
    /// The PKCE <c>code_challenge</c> (S256, the only method accepted): the token request's <c>code_verifier</c> must
    /// hash to it.
    /// </summary>
    [MaxLength(Constraints.MaxOAuthCodeChallengeLength)]
    public required string CodeChallenge { get; set; }

    /// <summary>
    /// The <c>resource</c> the token is asked for (RFC 8707) - the MCP server's URL. The token request must send the
    /// same one.
    /// </summary>
    [MaxLength(Constraints.MaxOAuthUrlLength)]
    public string? Resource { get; set; }

    public OAuthAuthorizationState State { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// How long the person has to answer and the client to redeem the code after that.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// SHA-256 of the authorization code, set on Allow - hashed like <see cref="ApiKey.KeyHash"/>.
    /// </summary>
    [MaxLength(64)]
    public string? CodeHash { get; set; }

    /// <summary>
    /// The person who allowed or denied it. With <see cref="OrganizationId"/>, this is the person's choice for the
    /// time between Allow and the token request, when the key doesn't exist yet. Once it does, <see cref="ApiKey"/>'s
    /// own organization and user are the source of truth.
    /// </summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>
    /// The organization the key is for - set on Allow, see <see cref="UserId"/>.
    /// </summary>
    public long? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    /// <summary>
    /// When the person allowed or denied it.
    /// </summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>
    /// When the client exchanged the code for its key.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// The issued key. Connecting the same client again for the same person and organization revokes it.
    /// </summary>
    public Guid? ApiKeyId { get; set; }
    public ApiKey? ApiKey { get; set; }
}

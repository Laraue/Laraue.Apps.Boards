using System.Text.Json;
using System.Text.Json.Serialization;

namespace Laraue.Apps.Boards.WebApiHost;

public sealed class TelegramWidgetAuthRequest
{
    /// <summary>
    /// Telegram user ID. Always present.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// User's first name. Always present but may be empty string.
    /// </summary>
    [JsonPropertyName("first_name")]
    public string FirstName { get; init; } = string.Empty;

    /// <summary>
    /// User's last name. Null if not set on the account.
    /// </summary>
    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    /// <summary>
    /// Telegram username without @. Null if user has no username.
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>
    /// HTTPS URL of the user's profile photo. Null if no photo or privacy settings hide it.
    /// </summary>
    [JsonPropertyName("photo_url")]
    public string? PhotoUrl { get; init; }

    /// <summary>
    /// Unix timestamp of when the auth was performed.
    /// Always present. Reject if older than 24 hours.
    /// </summary>
    [JsonPropertyName("auth_date")]
    public long AuthDate { get; init; }

    /// <summary>
    /// HMAC-SHA256 signature for verification. Always present.
    /// </summary>
    [JsonPropertyName("hash")]
    public string Hash { get; init; } = string.Empty;

    /// <summary>
    /// The first page the user opened on the site (BRD-295), saved by the frontend. Not a Telegram field, so it isn't
    /// signed and isn't part of the data-check-string; named so that it can't clash with a field Telegram may add.
    /// </summary>
    [JsonPropertyName("sign_up_landing_url")]
    public string? SignUpLandingUrl { get; init; }

    /// <summary>
    /// The site that sent the user to that page (BRD-295), saved by the frontend; not signed, like
    /// <see cref="SignUpLandingUrl"/>.
    /// </summary>
    [JsonPropertyName("sign_up_referrer")]
    public string? SignUpReferrer { get; init; }

    /// <summary>
    /// The fields Telegram sent that this class does not know yet. Telegram signs all the fields it
    /// sends, so they must be part of the data-check-string, or the hash of a request with a new
    /// field would not match.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}
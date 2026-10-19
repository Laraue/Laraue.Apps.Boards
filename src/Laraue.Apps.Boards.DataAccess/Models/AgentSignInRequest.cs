using System.ComponentModel.DataAnnotations;
using Laraue.Apps.Boards.DataAccess.Enums;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// One "connect this machine" sign-in of the agent runner (BRD-309), so `boards-agent setup` gets an
/// <see cref="ApiKey"/> without the person copying one. The runner starts it and polls with its secret device code;
/// the person opens the Allow page by <see cref="UserCode"/>, picks an organization and allows or denies it. On the
/// first poll after Allow, the key is created for that user and organization and returned once - the raw key is
/// never stored here.
/// Not OAuth: a real sign-in server belongs to Laraue.Apps.Identity; this only hands out a Boards key.
/// </summary>
public class AgentSignInRequest
{
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 of the device code, the runner's secret for polling - hashed like <see cref="ApiKey.KeyHash"/>.
    /// </summary>
    [MaxLength(64)]
    public required string DeviceCodeHash { get; set; }

    /// <summary>
    /// The short code in the Allow page's link, shown on the page and in the terminal so the person can compare
    /// them, e.g. <c>WXYZ-1234</c>.
    /// </summary>
    [MaxLength(Constraints.AgentSignInUserCodeLength)]
    public required string UserCode { get; set; }

    /// <summary>
    /// The runner's machine, as it reported it - shown on the Allow page and put in the key's name.
    /// </summary>
    [MaxLength(Constraints.MaxAgentSignInMachineNameLength)]
    public required string MachineName { get; set; }

    public AgentSignInState State { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The runner's last poll, to tell it to slow down when it polls faster than it was asked to.
    /// </summary>
    public DateTime? LastPolledAt { get; set; }

    /// <summary>
    /// The person who allowed or denied it. With <see cref="OrganizationId"/>, this is the person's choice for the
    /// time between Allow and the runner's next poll, when the key doesn't exist yet. Once it does,
    /// <see cref="ApiKey"/>'s own organization and user are the source of truth.
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
    /// When the runner received its key.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    public Guid? ApiKeyId { get; set; }
    public ApiKey? ApiKey { get; set; }
}

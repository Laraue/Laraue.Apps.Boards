using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A long-lived credential letting a program (e.g. an MCP client) act as <see cref="CreatedByUser"/>
/// within <see cref="Organization"/>, without a browser login. Permissions are never snapshotted -
/// every use resolves <see cref="CreatedByUserId"/>'s access live through the same
/// <c>IAccessService</c> checks a normal request would go through, so the key automatically loses
/// whatever access its owner loses.
/// </summary>
public class ApiKey
{
    public Guid Id { get; set; }

    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    [MaxLength(64)]
    public required string Name { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw secret. The raw secret itself is never stored - it's shown to the
    /// user once, at creation time, and is unrecoverable after that.
    /// </summary>
    [MaxLength(64)]
    public required string KeyHash { get; set; }

    /// <summary>
    /// First few characters of the raw secret, so a listing can tell keys apart without ever
    /// re-revealing one in full.
    /// </summary>
    [MaxLength(16)]
    public required string KeyPrefix { get; set; }

    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Last time this key successfully authenticated a request, or null if never used.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// UTC timestamp the key was revoked at, or null if it is still active. Not part of the
    /// six-entity soft-delete convention (see AGENTS.md) - just a plain revoked flag.
    /// </summary>
    public DateTime? RevokedAt { get; set; }
}

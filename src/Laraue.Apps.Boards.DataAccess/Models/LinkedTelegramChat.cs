namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A Telegram group/supergroup chat linked to an organization (and optionally a
/// space/epic/status) so that messages in it can be turned into cards.
/// </summary>
public class LinkedTelegramChat
{
    public long Id { get; set; }

    /// <summary>
    /// Telegram chat identifier.
    /// </summary>
    public required long ExternalChatId { get; init; }

    /// <summary>
    /// Best-effort display name of the chat.
    /// </summary>
    public string? Title { get; set; }

    public long? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public long? SpaceId { get; set; }
    public Space? Space { get; set; }

    /// <summary>
    /// Optional refinement narrowing the destination to a specific epic.
    /// Only meaningful when <see cref="SpaceId"/> is set.
    /// </summary>
    public long? EpicId { get; set; }
    public Epic? Epic { get; set; }

    /// <summary>
    /// Optional refinement narrowing the destination to a specific status.
    /// Only meaningful when <see cref="SpaceId"/> is set.
    /// </summary>
    public long? StatusId { get; set; }
    public Status? Status { get; set; }

    public Guid? LinkedByUserId { get; set; }
    public User? LinkedByUser { get; set; }

    public DateTime? LinkedAt { get; set; }
}

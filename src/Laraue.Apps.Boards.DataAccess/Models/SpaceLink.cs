using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.DataAccess.Models;

/// <summary>
/// A link a space keeps to something outside Boards - its repositories, docs, designs, dashboards (BRD-312) - with a
/// description of what it is and when it matters. People see the links on the space page; the agent runner gets them
/// as context and recognizes repositories by matching the URL against local checkouts' git remotes. A URL is listed
/// once per space; the same URL can be in other spaces with their own descriptions.
/// </summary>
public class SpaceLink
{
    public long Id { get; set; }

    public long SpaceId { get; set; }
    public Space? Space { get; set; }

    [MaxLength(Constraints.MaxSpaceLinkUrlLength)]
    public required string Url { get; set; }

    /// <summary>
    /// Markdown: what the link is and when it matters, e.g. "Nuxt web app; run pnpm pre-commit before committing".
    /// </summary>
    [MaxLength(Constraints.MaxSpaceLinkDescriptionLength)]
    public required string Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
}

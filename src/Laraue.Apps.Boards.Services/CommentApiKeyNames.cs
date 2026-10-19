using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

public static class CommentApiKeyNames
{
    /// <summary>
    /// The API key each of <paramref name="commentIds"/> was written through (e.g. an agent's), read from the
    /// comment's create entry in the change log - the record the history shows "via API key ..." from. One query
    /// for a whole page of comments; a comment written in the app has no entry in the result.
    /// </summary>
    public static Task<Dictionary<long, string>> GetCommentApiKeyNames(
        this DatabaseContext context,
        IReadOnlyCollection<long> commentIds,
        CancellationToken cancellationToken)
    {
        return context.OrganizationLogs
            .Where(l => l.EntityType == LogEntityType.Comment
                && l.Action == LogAction.Create
                && l.ApiKeyId != null
                && commentIds.Contains(l.EntityId!.Value))
            .Select(l => new { CommentId = l.EntityId!.Value, l.ApiKey!.Name })
            .ToDictionaryAsync(x => x.CommentId, x => x.Name, cancellationToken);
    }
}

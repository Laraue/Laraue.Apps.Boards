using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// What a board (epic) needs for its issues to be handed to the agent (BRD-305): a status in each of
/// <see cref="RequiredStatusCategories"/> - the agent's flow is not started -> in progress -> review, and the person
/// takes it from Review. The issue itself must be in a Created status when it's handed off.
/// </summary>
public static class AgentRunBoardRequirements
{
    public static readonly StatusCategory[] RequiredStatusCategories =
    [
        StatusCategory.Created,
        StatusCategory.InProgress,
        StatusCategory.Review,
    ];

    /// <summary>
    /// The required categories the epic has no active status in, in <see cref="RequiredStatusCategories"/> order;
    /// empty when the board is ready for the agent.
    /// </summary>
    public static async Task<StatusCategory[]> GetMissingStatusCategories(
        this DatabaseContext context,
        long epicId,
        CancellationToken cancellationToken)
    {
        var categories = await context.ActiveStatuses()
            .Where(x => x.EpicId == epicId)
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(cancellationToken);

        return RequiredStatusCategories
            .Except(categories)
            .ToArray();
    }
}

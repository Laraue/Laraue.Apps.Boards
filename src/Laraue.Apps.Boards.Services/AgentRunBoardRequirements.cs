using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Enums;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// What a board (epic) needs for its issues to be handed to the agent (BRD-305): a status in each of
/// <see cref="AgentRunBoardRequirements.RequiredStatusCategories"/> - the agent's flow is not started -> in
/// progress -> review, and the person takes it from Review. The issue itself must be in a Created status when it's
/// handed off. A plain read, used by <see cref="ICoreAgentRunsService"/> and the web issue detail.
/// </summary>
public interface IAgentRunBoardRequirements
{
    /// <summary>
    /// The required categories the epic has no active status in, in order; empty when the board is ready for the
    /// agent.
    /// </summary>
    Task<StatusCategory[]> GetMissingStatusCategories(long epicId, CancellationToken cancellationToken);
}

public class AgentRunBoardRequirements(DatabaseContext context) : IAgentRunBoardRequirements
{
    public static readonly StatusCategory[] RequiredStatusCategories =
    [
        StatusCategory.Created,
        StatusCategory.InProgress,
        StatusCategory.Review,
    ];

    public async Task<StatusCategory[]> GetMissingStatusCategories(long epicId, CancellationToken cancellationToken)
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

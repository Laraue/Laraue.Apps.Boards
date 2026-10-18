using Laraue.Apps.Boards.DataAccess.Models;

namespace Laraue.Apps.Boards.WebApiServices;

public static class AgentRunQueryExtensions
{
    /// <summary>
    /// Projects runs straight into <see cref="AgentRunDto"/> - shared by the hand-off response and the issue detail.
    /// </summary>
    public static IQueryable<AgentRunDto> SelectAgentRunDto(this IQueryable<IssueAgentRun> runs)
    {
        return runs.Select(r => new AgentRunDto
        {
            Id = r.Id,
            State = r.State,
            CreatedAt = r.CreatedAt,
            ClaimedAt = r.ClaimedAt,
            ClaimedByApiKeyName = r.ClaimedByApiKey!.Name,
            FinishedAt = r.FinishedAt,
            FailureReason = r.FailureReason,
        });
    }
}

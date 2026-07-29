using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.DataAccess.Models;
using LinqToDB.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.Services;

public interface IOrganizationConcurrencyControlService
{
    Task ExecuteIssueRankRelatedOperation(
        long organizationId,
        Func<Task> operation,
        CancellationToken ct);
}

public class OrganizationConcurrencyControlService(
    DatabaseContext context,
    ILogger<OrganizationConcurrencyControlService> logger) : IOrganizationConcurrencyControlService
{
    public async Task ExecuteIssueRankRelatedOperation(
        long organizationId,
        Func<Task> operation,
        CancellationToken ct)
    {
        // Pessimistic organization lock
        var lockKey = $"change-issues-order-{organizationId}";
        await context.Database.PgAdvisoryXactLock(lockKey, ct);
        
        try
        {
            await operation();
        }
        catch (RankSpaceExhaustedException e)
        {
            logger.LogWarning(
                e,
                "Rebalance LexoRank for organization: '{organizationId}' triggered",
                organizationId);
            
            await RebalanceOrganizationLexoRank(organizationId, ct);
            
            // Attempt #2 after the rebalance
            await operation();
        }
    }

    private async Task RebalanceOrganizationLexoRank(long organizationId, CancellationToken ct)
    {
        var issues = await context.Issues
            .Where(x => x.Status!.Epic!.Space!.OrganizationId == organizationId)
            .OrderBy(x => x.LexoRank)
            .Select(x => new Issue { Id = x.Id, LexoRank = x.LexoRank })
            .ToListAsyncEF(ct);

        var freshRanks = LexoRank.CreateEvenlySpaced(issues.Count);

        for (var i = 0; i < issues.Count; i++)
            issues[i].LexoRank = freshRanks[i].ToString();

        await context.SaveChangesAsync(ct);
    }
}
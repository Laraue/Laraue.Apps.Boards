using Laraue.Apps.Boards.DataAccess;
using Microsoft.EntityFrameworkCore;
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
        var issueIds = await context.ActiveIssues()
            .Where(x => x.Status!.Epic!.Space!.OrganizationId == organizationId)
            .OrderBy(x => x.LexoRank)
            .Select(x => x.Id)
            .ToArrayAsyncEF(ct);

        var freshRanks = LexoRank.CreateEvenlySpaced(issueIds.Length)
            .Select(x => x.ToString())
            .ToArray();

        // One statement for the whole organization. Issues projected into new entities aren't
        // tracked, so SaveChanges used to write nothing here and the retry failed again (BRD-389).
        await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE issues
             SET lexo_rank = fresh.rank
             FROM unnest({issueIds}, {freshRanks}) AS fresh(id, rank)
             WHERE issues.id = fresh.id
             """,
            ct);
    }
}
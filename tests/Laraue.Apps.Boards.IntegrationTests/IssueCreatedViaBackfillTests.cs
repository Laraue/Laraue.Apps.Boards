using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Migrations;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Runs <see cref="AddIssueCreatedVia"/>'s backfill on seeded issues - the migration itself ran on an empty test
/// database. The backfill touches every issue, so it runs in a transaction that is rolled back, leaving the other tests'
/// data as it was.
/// </summary>
[Collection("IntegrationTest")]
public class IssueCreatedViaBackfillTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task Backfill_ShouldSetCreatedViaFromTelegramMessageAndCreatingKey_WhenIssuesExist()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org => org
            .AddIssueToDefaultStatus(userId)
            .AddIssueToDefaultStatus(userId)
            .AddIssueToDefaultStatus(userId)
            .AddIssueToDefaultStatus(userId)
            .AddIssueToDefaultStatus(userId));

        var web = organization.GetIssueData(0, 0, 0, 0).Issue.Id;
        var webWithHistory = organization.GetIssueData(0, 0, 0, 1).Issue.Id;
        var telegram = organization.GetIssueData(0, 0, 0, 2).Issue.Id;
        var mcp = organization.GetIssueData(0, 0, 0, 3).Issue.Id;
        var agent = organization.GetIssueData(0, 0, 0, 4).Issue.Id;

        var db = testScope.Database;

        var message = new TelegramMessage { ExternalChatId = -1008888888888, ExternalMessageId = 1 };
        db.Add(message);

        var mcpKey = CreateApiKey(organization.Id, userId, "My key");
        var agentKey = CreateApiKey(organization.Id, userId, "Claude Code agent (dev-laptop)");
        db.AddRange(mcpKey, agentKey);
        db.Add(new AgentSignInRequest
        {
            DeviceCodeHash = Guid.NewGuid().ToString("N"),
            UserCode = Guid.NewGuid().ToString("N")[..9],
            ClientName = "Claude Code agent",
            MachineName = "dev-laptop",
            State = AgentSignInState.Completed,
            ApiKey = agentKey,
        });

        db.AddRange(
            CreateLog(organization.Id, userId, webWithHistory, apiKeyId: null),
            CreateLog(organization.Id, userId, mcp, mcpKey.Id),
            CreateLog(organization.Id, userId, agent, agentKey.Id));
        await db.SaveChangesAsync();

        await db.Issues
            .Where(x => x.Id == telegram)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.TelegramMessageId, message.Id));

        var createdVia = await RunBackfill(testScope, web, webWithHistory, telegram, mcp, agent);

        Assert.Equal((IssueCreatedVia.Web, (Guid?)null), createdVia[web]);
        Assert.Equal((IssueCreatedVia.Web, (Guid?)null), createdVia[webWithHistory]);
        Assert.Equal((IssueCreatedVia.Telegram, (Guid?)null), createdVia[telegram]);
        Assert.Equal((IssueCreatedVia.Mcp, (Guid?)mcpKey.Id), createdVia[mcp]);
        Assert.Equal((IssueCreatedVia.Agent, (Guid?)agentKey.Id), createdVia[agent]);
    }

    private static ApiKey CreateApiKey(long organizationId, Guid userId, string name)
    {
        return new ApiKey
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CreatedByUserId = userId,
            Name = name,
            KeyHash = Guid.NewGuid().ToString("N"),
            KeyPrefix = Guid.NewGuid().ToString("N")[..16],
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static OrganizationLog CreateLog(long organizationId, Guid userId, long issueId, Guid? apiKeyId)
    {
        return new OrganizationLog
        {
            OrganizationId = organizationId,
            OwnerId = userId,
            ApiKeyId = apiKeyId,
            EntityId = issueId,
            EntityType = LogEntityType.Issue,
            Action = LogAction.Create,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static async Task<Dictionary<long, (IssueCreatedVia Via, Guid? KeyId)>> RunBackfill(
        WebApiTestHostScope testScope,
        params long[] issueIds)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();

        await testScope.Database.Database.ExecuteSqlRawAsync(AddIssueCreatedVia.BackfillSql);

        // The seeding context may still track these issues with their pre-backfill values.
        var createdVia = await testScope.Database.Issues
            .AsNoTracking()
            .Where(x => issueIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => (x.CreatedVia, x.CreatedByApiKeyId));

        await transaction.RollbackAsync();

        return createdVia;
    }
}

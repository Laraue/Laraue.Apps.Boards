using Laraue.Apps.Boards.Common;
using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.History;
using Laraue.Core.DataAccess.Contracts;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class CoreIssueLinksServiceTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task AddLink_ShouldStoreBlocksFromTheOtherIssue_WhenAddedAsBlockedBy()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 2);

        await AddLink(testScope, setup.Issues[0], IssueLinkRelation.BlockedBy, setup.Issues[1], setup.UserId);

        var link = await testScope.Database.IssueLinks.AsNoTracking().SingleAsync(x => x.SourceIssueId == setup.Issues[1].Id);
        Assert.Equal(IssueLinkType.Blocks, link.Type);
        Assert.Equal(setup.Issues[0].Id, link.TargetIssueId);
    }

    [Fact]
    public async Task AddLink_ShouldStoreLowerIdAsSource_WhenRelatesToIsAddedFromTheHigherId()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 2);
        var lower = setup.Issues[0].Id < setup.Issues[1].Id ? setup.Issues[0] : setup.Issues[1];
        var higher = lower == setup.Issues[0] ? setup.Issues[1] : setup.Issues[0];

        await AddLink(testScope, higher, IssueLinkRelation.RelatesTo, lower, setup.UserId);

        var link = await testScope.Database.IssueLinks.AsNoTracking().SingleAsync(x => x.SourceIssueId == lower.Id);
        Assert.Equal(IssueLinkType.RelatesTo, link.Type);
        Assert.Equal(higher.Id, link.TargetIssueId);
    }

    [Fact]
    public async Task AddLink_ShouldRecordTheLinkOnBothIssues_FromEachIssueSide()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 2);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(setup.Issues[0].OrganizationId, setup.UserId, "Claude", CancellationToken.None);

        await AddLink(testScope, setup.Issues[0], IssueLinkRelation.Duplicates, setup.Issues[1], new Actor(setup.UserId, apiKey.Id));

        var duplicateChange = await GetSingleLinkChange(testScope, setup.Issues[0]);
        Assert.Equal(IssueLinkRelation.Duplicates, duplicateChange.Change.Relation);
        Assert.Equal(setup.Issues[1].Key, duplicateChange.Change.IssueKey);
        Assert.Equal(IssueLinkAction.Added, duplicateChange.Change.Action);
        Assert.Equal("Claude", duplicateChange.ApiKeyName);

        var originalChange = await GetSingleLinkChange(testScope, setup.Issues[1]);
        Assert.Equal(IssueLinkRelation.DuplicatedBy, originalChange.Change.Relation);
        Assert.Equal(setup.Issues[0].Key, originalChange.Change.IssueKey);
        Assert.Equal(IssueLinkAction.Added, originalChange.Change.Action);
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenTheIssuesAreAlreadyLinkedInAnyWay()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 2);
        await AddLink(testScope, setup.Issues[0], IssueLinkRelation.Blocks, setup.Issues[1], setup.UserId);

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, setup.Issues[1], IssueLinkRelation.RelatesTo, setup.Issues[0], setup.UserId));

        // The existing link, from the side of the issue the new one was added from.
        Assert.Contains($"({setup.Issues[1].Key} BlockedBy {setup.Issues[0].Key})", exception.Errors["otherIssueId"]![0]);
        Assert.Equal(1, await testScope.Database.IssueLinks.CountAsync(x => x.SourceIssueId == setup.Issues[0].Id));
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenTheIssueIsLinkedToItself()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, setup.Issues[0], IssueLinkRelation.RelatesTo, setup.Issues[0], setup.UserId));
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenBlocksLinksWouldFormACycle()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 3);
        await AddLink(testScope, setup.Issues[0], IssueLinkRelation.Blocks, setup.Issues[1], setup.UserId);
        await AddLink(testScope, setup.Issues[1], IssueLinkRelation.Blocks, setup.Issues[2], setup.UserId);

        await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, setup.Issues[0], IssueLinkRelation.BlockedBy, setup.Issues[2], setup.UserId));
    }

    [Fact]
    public async Task AddLink_ShouldSkipDeletedIssues_WhenLookingForACycle()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 3);
        await AddLink(testScope, setup.Issues[0], IssueLinkRelation.Blocks, setup.Issues[1], setup.UserId);
        await AddLink(testScope, setup.Issues[1], IssueLinkRelation.Blocks, setup.Issues[2], setup.UserId);
        await testScope.Database.Issues
            .Where(x => x.Id == setup.Issues[1].Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.DeletedAt, DateTime.UtcNow));

        await AddLink(testScope, setup.Issues[2], IssueLinkRelation.Blocks, setup.Issues[0], setup.UserId);

        Assert.True(await testScope.Database.IssueLinks
            .AnyAsync(x => x.SourceIssueId == setup.Issues[2].Id && x.TargetIssueId == setup.Issues[0].Id));
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenTheIssuesBelongToDifferentOrganizations()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 1);
        var otherSetup = await Init(testScope, issueCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, setup.Issues[0], IssueLinkRelation.RelatesTo, otherSetup.Issues[0], setup.UserId));
    }

    [Fact]
    public async Task RemoveLink_ShouldRemoveTheLinkAndRecordItOnBothIssues_WhenRemovedFromTheTarget()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 2);
        await AddLink(testScope, setup.Issues[0], IssueLinkRelation.Blocks, setup.Issues[1], setup.UserId);

        await RemoveLink(testScope, setup.Issues[1], setup.Issues[0], setup.UserId);

        Assert.False(await testScope.Database.IssueLinks.AnyAsync(x => x.SourceIssueId == setup.Issues[0].Id));

        var blockerHistory = await GetLinkChanges(testScope, setup.Issues[0]);
        Assert.Equal(
            [
                new LinkChangeLine(IssueLinkRelation.Blocks, IssueLinkAction.Removed),
                new LinkChangeLine(IssueLinkRelation.Blocks, IssueLinkAction.Added),
            ],
            blockerHistory
                .Select(x => new LinkChangeLine(x.Change.Relation, x.Change.Action)));

        var blockedHistory = await GetLinkChanges(testScope, setup.Issues[1]);
        Assert.Equal(
            [
                new LinkChangeLine(IssueLinkRelation.BlockedBy, IssueLinkAction.Removed),
                new LinkChangeLine(IssueLinkRelation.BlockedBy, IssueLinkAction.Added),
            ],
            blockedHistory
                .Select(x => new LinkChangeLine(x.Change.Relation, x.Change.Action)));
    }

    [Fact]
    public async Task RemoveLink_ShouldThrowNotFound_WhenTheIssuesAreNotLinked()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 2);

        await Assert.ThrowsAsync<NotFoundException>(
            () => RemoveLink(testScope, setup.Issues[0], setup.Issues[1], setup.UserId));
    }

    [Fact]
    public async Task IssueLinksTable_ShouldRejectASecondLinkForTheSamePair_InTheOtherDirection()
    {
        using var testScope = host.CreateTestScope();
        var setup = await Init(testScope, issueCount: 2);
        await AddLink(testScope, setup.Issues[0], IssueLinkRelation.Duplicates, setup.Issues[1], setup.UserId);

        testScope.Database.IssueLinks.Add(new IssueLink
        {
            Type = IssueLinkType.Blocks,
            SourceIssueId = setup.Issues[1].Id,
            TargetIssueId = setup.Issues[0].Id,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = setup.UserId,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => testScope.Database.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(DataAccess.Constraints.IssueLinkPairIndexName, postgresException.ConstraintName);
    }

    private sealed record TestIssue(long Id, string Key, long OrganizationId);

    /// <summary>The organization's owner and the issues created in its default status, in order.</summary>
    private sealed record TestSetup(Guid UserId, TestIssue[] Issues);

    /// <summary>A link change as a history line shows it: the relation from the issue's side and the action.</summary>
    private sealed record LinkChangeLine(IssueLinkRelation Relation, IssueLinkAction Action);

    private static async Task<TestSetup> Init(WebApiTestHostScope testScope, int issueCount)
    {
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId, org =>
        {
            for (var i = 0; i < issueCount; i++)
                org.AddIssueToDefaultStatus(userId);
        });

        var issues = Enumerable.Range(0, issueCount)
            .Select(i => organization.GetIssueData(0, 0, 0, i))
            .Select(x => new TestIssue(x.Issue.Id, x.Key, organization.Id))
            .ToArray();

        return new TestSetup(userId, issues);
    }

    private static async Task AddLink(
        WebApiTestHostScope testScope,
        TestIssue issue,
        IssueLinkRelation relation,
        TestIssue otherIssue,
        Actor actor)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        await testScope.Services.GetRequiredService<ICoreIssueLinksService>()
            .AddLink(issue.Id, relation, otherIssue.Id, actor, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private static async Task RemoveLink(
        WebApiTestHostScope testScope,
        TestIssue issue,
        TestIssue otherIssue,
        Actor actor)
    {
        await using var transaction = await testScope.Database.Database.BeginTransactionAsync();
        await testScope.Services.GetRequiredService<ICoreIssueLinksService>()
            .RemoveLink(issue.Id, otherIssue.Id, actor, CancellationToken.None);
        await transaction.CommitAsync();
    }

    private sealed record LinkChange(IssueHistoryLinkChange Change, string? ApiKeyName);

    /// <summary>The issue's link changes, newest first.</summary>
    private static async Task<LinkChange[]> GetLinkChanges(WebApiTestHostScope testScope, TestIssue issue)
    {
        var history = await testScope.Services.GetRequiredService<IOrganizationHistoryReader>()
            .GetIssueHistory(
                issue.OrganizationId,
                issue.Id,
                issue.Key,
                new PaginationData { Page = 0, PerPage = 20 },
                CancellationToken.None);

        return history.Data
            .SelectMany(item => item.Changes
                .OfType<IssueHistoryLinkChange>()
                .Select(change => new LinkChange(change, item.ApiKeyName)))
            .ToArray();
    }

    private static async Task<LinkChange> GetSingleLinkChange(WebApiTestHostScope testScope, TestIssue issue)
    {
        return Assert.Single(await GetLinkChanges(testScope, issue));
    }
}

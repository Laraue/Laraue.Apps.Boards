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
        var (userId, issues) = await Init(testScope, issueCount: 2);

        await AddLink(testScope, issues[0], IssueLinkRelation.BlockedBy, issues[1], userId);

        var link = await testScope.Database.IssueLinks.AsNoTracking().SingleAsync(x => x.SourceIssueId == issues[1].Id);
        Assert.Equal(IssueLinkType.Blocks, link.Type);
        Assert.Equal(issues[0].Id, link.TargetIssueId);
    }

    [Fact]
    public async Task AddLink_ShouldStoreLowerIdAsSource_WhenRelatesToIsAddedFromTheHigherId()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 2);
        var (lower, higher) = issues[0].Id < issues[1].Id ? (issues[0], issues[1]) : (issues[1], issues[0]);

        await AddLink(testScope, higher, IssueLinkRelation.RelatesTo, lower, userId);

        var link = await testScope.Database.IssueLinks.AsNoTracking().SingleAsync(x => x.SourceIssueId == lower.Id);
        Assert.Equal(IssueLinkType.RelatesTo, link.Type);
        Assert.Equal(higher.Id, link.TargetIssueId);
    }

    [Fact]
    public async Task AddLink_ShouldRecordTheLinkOnBothIssues_FromEachIssueSide()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 2);
        var apiKey = await testScope.Services.GetRequiredService<ICoreApiKeysService>()
            .CreateAsync(issues[0].OrganizationId, userId, "Claude", CancellationToken.None);

        await AddLink(testScope, issues[0], IssueLinkRelation.Duplicates, issues[1], new Actor(userId, apiKey.Id));

        var duplicateChange = await GetSingleLinkChange(testScope, issues[0]);
        Assert.Equal(IssueLinkRelation.Duplicates, duplicateChange.Change.Relation);
        Assert.Equal(issues[1].Key, duplicateChange.Change.IssueKey);
        Assert.Equal(IssueLinkAction.Added, duplicateChange.Change.Action);
        Assert.Equal("Claude", duplicateChange.ApiKeyName);

        var originalChange = await GetSingleLinkChange(testScope, issues[1]);
        Assert.Equal(IssueLinkRelation.DuplicatedBy, originalChange.Change.Relation);
        Assert.Equal(issues[0].Key, originalChange.Change.IssueKey);
        Assert.Equal(IssueLinkAction.Added, originalChange.Change.Action);
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenTheIssuesAreAlreadyLinkedInAnyWay()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 2);
        await AddLink(testScope, issues[0], IssueLinkRelation.Blocks, issues[1], userId);

        await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, issues[1], IssueLinkRelation.RelatesTo, issues[0], userId));

        Assert.Equal(1, await testScope.Database.IssueLinks.CountAsync(x => x.SourceIssueId == issues[0].Id));
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenTheIssueIsLinkedToItself()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, issues[0], IssueLinkRelation.RelatesTo, issues[0], userId));
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenBlocksLinksWouldFormACycle()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 3);
        await AddLink(testScope, issues[0], IssueLinkRelation.Blocks, issues[1], userId);
        await AddLink(testScope, issues[1], IssueLinkRelation.Blocks, issues[2], userId);

        await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, issues[0], IssueLinkRelation.BlockedBy, issues[2], userId));
    }

    [Fact]
    public async Task AddLink_ShouldSkipDeletedIssues_WhenLookingForACycle()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 3);
        await AddLink(testScope, issues[0], IssueLinkRelation.Blocks, issues[1], userId);
        await AddLink(testScope, issues[1], IssueLinkRelation.Blocks, issues[2], userId);
        await testScope.Database.Issues
            .Where(x => x.Id == issues[1].Id)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.DeletedAt, DateTime.UtcNow));

        await AddLink(testScope, issues[2], IssueLinkRelation.Blocks, issues[0], userId);

        Assert.True(await testScope.Database.IssueLinks
            .AnyAsync(x => x.SourceIssueId == issues[2].Id && x.TargetIssueId == issues[0].Id));
    }

    [Fact]
    public async Task AddLink_ShouldFail_WhenTheIssuesBelongToDifferentOrganizations()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 1);
        var (_, otherIssues) = await Init(testScope, issueCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(
            () => AddLink(testScope, issues[0], IssueLinkRelation.RelatesTo, otherIssues[0], userId));
    }

    [Fact]
    public async Task RemoveLink_ShouldRemoveTheLinkAndRecordItOnBothIssues_WhenRemovedFromTheTarget()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 2);
        await AddLink(testScope, issues[0], IssueLinkRelation.Blocks, issues[1], userId);

        await RemoveLink(testScope, issues[1], issues[0], userId);

        Assert.False(await testScope.Database.IssueLinks.AnyAsync(x => x.SourceIssueId == issues[0].Id));

        var blockerHistory = await GetLinkChanges(testScope, issues[0]);
        Assert.Equal(
            [(IssueLinkRelation.Blocks, IssueLinkAction.Removed), (IssueLinkRelation.Blocks, IssueLinkAction.Added)],
            blockerHistory.Select(x => (x.Change.Relation, x.Change.Action)));

        var blockedHistory = await GetLinkChanges(testScope, issues[1]);
        Assert.Equal(
            [(IssueLinkRelation.BlockedBy, IssueLinkAction.Removed), (IssueLinkRelation.BlockedBy, IssueLinkAction.Added)],
            blockedHistory.Select(x => (x.Change.Relation, x.Change.Action)));
    }

    [Fact]
    public async Task RemoveLink_ShouldThrowNotFound_WhenTheIssuesAreNotLinked()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 2);

        await Assert.ThrowsAsync<NotFoundException>(
            () => RemoveLink(testScope, issues[0], issues[1], userId));
    }

    [Fact]
    public async Task IssueLinksTable_ShouldRejectASecondLinkForTheSamePair_InTheOtherDirection()
    {
        using var testScope = host.CreateTestScope();
        var (userId, issues) = await Init(testScope, issueCount: 2);
        await AddLink(testScope, issues[0], IssueLinkRelation.Duplicates, issues[1], userId);

        testScope.Database.IssueLinks.Add(new IssueLink
        {
            Type = IssueLinkType.Blocks,
            SourceIssueId = issues[1].Id,
            TargetIssueId = issues[0].Id,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => testScope.Database.SaveChangesAsync());
        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(DataAccess.Constraints.IssueLinkPairIndexName, postgresException.ConstraintName);
    }

    private sealed record TestIssue(long Id, string Key, long OrganizationId);

    private static async Task<(Guid UserId, TestIssue[] Issues)> Init(WebApiTestHostScope testScope, int issueCount)
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

        return (userId, issues);
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

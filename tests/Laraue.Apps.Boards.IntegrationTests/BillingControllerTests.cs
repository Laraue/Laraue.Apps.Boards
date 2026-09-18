using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.DataAccess.Contracts;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class BillingControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<BillingController> _billingController = host.Controller<BillingController>();

    [Fact]
    public async Task GetSummary_ShouldCombineSubscriptionAndBalance_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        host.BillingSubscriptionClientMock
            .Setup(x => x.GetActiveSubscriptionAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSubscriptionInfo
            {
                Code = "personal_free",
                IsPersonal = true,
                LimitIssuesPerMonth = 100,
                LimitFreeTeamOrganizationsCount = 3,
                IncludedTokensCount = 2_500_000,
            });

        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance
            {
                FreeTokensCount = 1000,
                SubscriptionTokensCount = 2_300_000,
                PurchasedTokensCount = 50_000,
            });

        var now = DateTime.UtcNow;
        testScope.Database.IssueMonthlyCounts.Add(new IssueMonthlyCount
        {
            OrganizationId = organization.Id,
            Year = now.Year,
            Month = now.Month,
            Count = 4,
        });
        await testScope.Database.SaveChangesAsync();

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        Assert.Equal("personal_free", summary!.SubscriptionCode);

        Assert.NotNull(summary.IssuesPerMonth);
        Assert.Equal(100, summary.IssuesPerMonth!.Limit);
        Assert.Equal(4, summary.IssuesPerMonth.Used);
        Assert.Equal(96, summary.IssuesPerMonth.Remaining);

        Assert.Equal(2_500_000, summary.Tokens.Limit);
        Assert.Equal(200_000, summary.Tokens.Used);
        Assert.Equal(2_351_000, summary.Tokens.Remaining);

        var personalSummary = Assert.IsType<PersonalBillingSummary>(summary);

        // The organization created by InitializeOrganization is itself a team org owned by
        // userId, so it already counts toward their owned-team-orgs usage.
        Assert.NotNull(personalSummary.FreeTeamOrganizations);
        Assert.Equal(3, personalSummary.FreeTeamOrganizations!.Limit);
        Assert.Equal(1, personalSummary.FreeTeamOrganizations.Used);
        Assert.Equal(2, personalSummary.FreeTeamOrganizations.Remaining);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnTeamSummaryWithNoFreeTeamOrganizationsField_WhenOrganizationIsTeam()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        host.BillingSubscriptionClientMock
            .Setup(x => x.GetActiveSubscriptionAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSubscriptionInfo
            {
                Code = "team_free",
                IsPersonal = false,
                LimitIssuesPerMonth = null,
                IncludedTokensCount = 2_500_000,
            });

        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance
            {
                FreeTokensCount = 0,
                SubscriptionTokensCount = 2_500_000,
                PurchasedTokensCount = 0,
            });

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        Assert.IsType<TeamBillingSummary>(summary);
        Assert.Null(summary!.IssuesPerMonth);
        Assert.Equal(0, summary.Tokens.Used);
    }

    [Fact]
    public async Task GetTransactions_ShouldReturnPagedItems_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        var transactionId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        host.BillingTokenClientMock
            .Setup(x => x.GetTransactionsAsync(
                organization.Id, userId, It.IsAny<PaginationData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShortPaginatedResult<TokenTransactionItem>(
                page: 0,
                perPage: 10,
                hasNextPage: false,
                data:
                [
                    new TokenTransactionItem
                    {
                        Id = transactionId,
                        OwnerId = userId,
                        Status = TokenTransactionStatus.Confirmed,
                        Reason = TokenTransactionReason.Spend,
                        CreatedAt = createdAt,
                        FinishedAt = createdAt,
                        Delta = -42,
                    },
                ]));

        var request = new GetBillingTransactionsRequest
        {
            Pagination = new PaginationData { Page = 0, PerPage = 10 },
        };

        var page = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetTransactions(request));

        var item = Assert.Single(page!.Data);
        Assert.Equal(transactionId, item.Id);
        Assert.Equal(TokenTransactionStatus.Confirmed, item.Status);
        Assert.Equal(TokenTransactionReason.Spend, item.Reason);
        Assert.Equal(-42, item.Delta);
        Assert.False(page.HasNextPage);
    }
}

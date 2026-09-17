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
                LimitIssuesPerMonth = 100,
                LimitFreeTeamOrganizationsCount = 3,
            });

        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance
            {
                FreeTokensCount = 1000,
                SubscriptionTokensCount = 2_500_000,
                PurchasedTokensCount = 0,
            });

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        Assert.Equal("personal_free", summary!.SubscriptionCode);
        Assert.Equal(100, summary.LimitIssuesPerMonth);
        Assert.Equal(3, summary.LimitFreeTeamOrganizationsCount);
        Assert.Equal(1000, summary.FreeTokensCount);
        Assert.Equal(2_500_000, summary.SubscriptionTokensCount);
        Assert.Equal(0, summary.PurchasedTokensCount);
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

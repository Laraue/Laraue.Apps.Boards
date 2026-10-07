using System.Net;
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
    public async Task GetTariffName_ShouldReturnTariffCode_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        host.BillingSubscriptionClientMock
            .Setup(x => x.GetTariffNameAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Team");

        var tariff = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetTariffName());

        Assert.Equal("Team", tariff!.Name);
    }

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
                LimitPeriodStartedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            });

        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance
            {
                FreeTokensCount = 1000,
                SubscriptionTokensCount = 2_300_000,
                PurchasedTokensCount = 50_000,
            });

        testScope.Database.IssuePeriodCounts.Add(new IssuePeriodCount
        {
            OrganizationId = organization.Id,
            PeriodStartedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            Count = 4,
        });
        await testScope.Database.SaveChangesAsync();

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        Assert.Equal("personal_free", summary!.SubscriptionCode);
        Assert.True(summary.CanPay);

        Assert.NotNull(summary.IssuesPerMonth);
        Assert.Equal(100, summary.IssuesPerMonth!.Limit);
        Assert.Equal(4, summary.IssuesPerMonth.Used);
        Assert.Equal(96, summary.IssuesPerMonth.Remaining);

        Assert.Equal(2_500_000, summary.Tokens.Limit);
        // 2,500,000 included - (2,300,000 subscription + 1,000 free) left of the plan.
        Assert.Equal(199_000, summary.Tokens.Used);
        // The plan's own tokens left, the purchased ones are reported on their own.
        Assert.Equal(2_301_000, summary.Tokens.Remaining);
        Assert.Equal(50_000, summary.PurchasedTokensCount);

        var personalSummary = Assert.IsType<PersonalBillingSummary>(summary);

        // The organization created by InitializeOrganization is itself a team org owned by
        // userId, so it already counts toward their owned-team-orgs usage.
        Assert.NotNull(personalSummary.FreeTeamOrganizations);
        Assert.Equal(3, personalSummary.FreeTeamOrganizations!.Limit);
        Assert.Equal(1, personalSummary.FreeTeamOrganizations.Used);
        Assert.Equal(2, personalSummary.FreeTeamOrganizations.Remaining);
    }

    [Fact]
    public async Task GetSummary_ShouldShowNothingUsed_WhenFreePlanAllowanceIsUntouched()
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
                IncludedTokensCount = 25_000,
                LimitPeriodStartedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            });

        // A Free plan's allowance lives in the free bucket, the subscription one stays empty.
        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance
            {
                FreeTokensCount = 25_000,
                SubscriptionTokensCount = 0,
                PurchasedTokensCount = 0,
            });

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        Assert.Equal(25_000, summary!.Tokens.Limit);
        Assert.Equal(0, summary.Tokens.Used);
        Assert.Equal(25_000, summary.Tokens.Remaining);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnThePeriodAndTheExpiryOfThePurchasedTokens_WhenBillingReportsThem()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var periodEndsAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var expireAt = new DateTime(2027, 4, 6, 0, 0, 0, DateTimeKind.Utc);

        host.BillingSubscriptionClientMock
            .Setup(x => x.GetActiveSubscriptionAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSubscriptionInfo
            {
                Code = "personal_free",
                IsPersonal = true,
                IncludedTokensCount = 25_000,
                LimitPeriodStartedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEndsAt = periodEndsAt,
                PeriodResets = true,
            });

        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance
            {
                FreeTokensCount = 25_000,
                SubscriptionTokensCount = 0,
                PurchasedTokensCount = 125_000,
                PurchasedTokensExpireAt = expireAt,
                PurchasedTokensExpiringCount = 25_000,
            });

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        // The big number and the usage agree: nothing used of the plan's 25,000, all of it left.
        Assert.Equal(0, summary!.Tokens.Used);
        Assert.Equal(25_000, summary.Tokens.Remaining);
        Assert.Equal(125_000, summary.PurchasedTokensCount);
        Assert.Equal(expireAt, summary.PurchasedTokensExpireAt);
        Assert.Equal(25_000, summary.PurchasedTokensExpiringCount);
        Assert.Equal(periodEndsAt, summary.PeriodEndsAt);
        Assert.True(summary.PeriodResets);
    }

    [Fact]
    public async Task GetSummary_ShouldCountIssuesInThePeriodBillingReports_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var currentPeriod = new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);

        testScope.Database.IssuePeriodCounts.AddRange(
            new IssuePeriodCount { OrganizationId = organization.Id, PeriodStartedAt = currentPeriod.AddMonths(-1), Count = 90 },
            new IssuePeriodCount { OrganizationId = organization.Id, PeriodStartedAt = currentPeriod, Count = 7 });
        await testScope.Database.SaveChangesAsync();

        host.BillingSubscriptionClientMock
            .Setup(x => x.GetActiveSubscriptionAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSubscriptionInfo
            {
                Code = "personal_free",
                IsPersonal = true,
                LimitIssuesPerMonth = 100,
                IncludedTokensCount = 25_000,
                LimitPeriodStartedAt = currentPeriod,
            });

        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance { FreeTokensCount = 25_000, SubscriptionTokensCount = 0, PurchasedTokensCount = 0 });

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        // Only the rows of the current period count, the 90 of the period before are not.
        Assert.Equal(7, summary!.IssuesPerMonth!.Used);
        Assert.Equal(93, summary.IssuesPerMonth.Remaining);
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
                LimitPeriodStartedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
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

    [Fact]
    public async Task CreateCheckout_ShouldReturnBillingUrl_WhenCallerIsOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var itemId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        host.BillingPaymentClientMock
            .Setup(x => x.CreateCheckoutAsync(
                organization.Id,
                userId,
                BillingItemKind.Subscription,
                itemId,
                "RUB",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingCheckout(paymentId, "https://pay.example/checkout"));

        var checkout = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.CreateCheckout(new CreateCheckoutRequest
            {
                Kind = BillingItemKind.Subscription,
                ItemId = itemId,
                CurrencyCode = "rub",
            }));

        Assert.Equal(paymentId, checkout!.PaymentId);
        Assert.Equal("https://pay.example/checkout", checkout.Url);
    }

    [Fact]
    public async Task CreateCheckout_ShouldBeForbidden_WhenCallerIsNotOrganizationOwner()
    {
        using var testScope = host.CreateTestScope();
        var ownerId = await testScope.CreateUser();
        var memberId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => org
            .AddUser(memberId, builder => builder
                .SetGlobalAccessLevel(x => x.CanRead = true)));

        host.BillingPaymentClientMock.Invocations.Clear();

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _billingController
            .WithOrganizationAuthorization(organization.Id, memberId)
            .Execute(x => x.CreateCheckout(new CreateCheckoutRequest
            {
                Kind = BillingItemKind.Subscription,
                ItemId = Guid.NewGuid(),
                CurrencyCode = "RUB",
            })));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        host.BillingPaymentClientMock.Verify(
            x => x.CreateCheckoutAsync(
                It.IsAny<long>(),
                It.IsAny<Guid>(),
                It.IsAny<BillingItemKind>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

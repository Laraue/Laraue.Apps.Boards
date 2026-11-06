using System.Net;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Billing;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class StorageQuotaTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();
    private readonly Proxy<BillingController> _billingController = host.Controller<BillingController>();

    [Fact]
    public async Task Create_ShouldBePaymentRequiredAndStoreNothing_WhenFileDoesNotFitTheQuota()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        SetupQuota(organization.Id, quotaBytes: 10);
        var request = CreateRequestWithFile(organization.GetStatus(0, 0, 0).Id, userId);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(request)));

        Assert.Equal(HttpStatusCode.PaymentRequired, ex.StatusCode);
        Assert.False(await testScope.Database.Issues.AnyAsync(x => x.Content == "With a file" && x.StatusId == request.StatusId));
        Assert.DoesNotContain(host.StorageClient.Files.Values, x => x.OrganizationId == organization.Id);
    }

    [Fact]
    public async Task Create_ShouldStoreTheFile_WhenItFitsTheQuota()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        SetupQuota(organization.Id, quotaBytes: 100L * 1024 * 1024);
        var request = CreateRequestWithFile(organization.GetStatus(0, 0, 0).Id, userId);

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(request));

        Assert.Contains(host.StorageClient.Files.Values, x => x.OrganizationId == organization.Id);
    }

    [Fact]
    public async Task GetSummary_ShouldReturnStorageUsedAndQuota_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var addonId = Guid.NewGuid();
        SetupQuota(organization.Id, quotaBytes: 11L * 1024 * 1024 * 1024, addonId);
        host.BillingTokenClientMock
            .Setup(x => x.GetBalanceAsync(organization.Id, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenBalance { FreeTokensCount = 0, SubscriptionTokensCount = 0, PurchasedTokensCount = 0 });
        var request = CreateRequestWithFile(organization.GetStatus(0, 0, 0).Id, userId);

        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(request));

        var summary = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetSummary());

        var stored = host.StorageClient.Files.Values
            .Where(x => x.OrganizationId == organization.Id)
            .Sum(x => (long)x.Content.Length);
        Assert.Equal(stored, summary!.Storage.UsedBytes);
        Assert.Equal(11L * 1024 * 1024 * 1024, summary.Storage.QuotaBytes);
        Assert.Equal(addonId, summary.Storage.StorageAddonId);
    }

    [Fact]
    public async Task CreateCheckout_ShouldPassTheAddonAndTheUsage_WhenTariffIsBought()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var tariffId = Guid.NewGuid();
        var addonId = Guid.NewGuid();

        host.BillingPaymentClientMock
            .Setup(x => x.CreateCheckoutAsync(
                organization.Id,
                userId,
                BillingItemKind.Subscription,
                tariffId,
                "RUB",
                new BillingStorageCheckout(addonId, 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingCheckout(Guid.NewGuid(), "https://pay.example/checkout"));

        var checkout = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.CreateCheckout(new CreateCheckoutRequest
            {
                Kind = BillingItemKind.Subscription,
                ItemId = tariffId,
                CurrencyCode = "RUB",
                StorageAddonId = addonId,
            }));

        Assert.Equal("https://pay.example/checkout", checkout!.Url);
        Assert.Null(checkout.StorageOverLimit);
    }

    [Fact]
    public async Task CreateCheckout_ShouldSayHowMuchToDelete_WhenTheNewStorageIsBelowTheUsage()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var tariffId = Guid.NewGuid();

        host.BillingPaymentClientMock
            .Setup(x => x.CreateCheckoutAsync(
                organization.Id,
                userId,
                BillingItemKind.Subscription,
                tariffId,
                "RUB",
                It.IsAny<BillingStorageCheckout?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StorageOverLimitException(usageBytes: 1500, quotaBytes: 1024));

        var checkout = await _billingController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.CreateCheckout(new CreateCheckoutRequest
            {
                Kind = BillingItemKind.Subscription,
                ItemId = tariffId,
                CurrencyCode = "RUB",
            }));

        Assert.Null(checkout!.Url);
        Assert.Null(checkout.PaymentId);
        Assert.Equal(1500, checkout.StorageOverLimit!.UsedBytes);
        Assert.Equal(1024, checkout.StorageOverLimit.QuotaBytes);
        Assert.Equal(476, checkout.StorageOverLimit.ExcessBytes);
    }

    private static CreateIssueRequest CreateRequestWithFile(long statusId, Guid userId) => new()
    {
        Title = "Title",
        Content = "With a file",
        StatusId = statusId,
        AssigneeId = userId,
        Files = [FormFileUtility.GetFormFile("image.jpg")],
    };

    private void SetupQuota(long organizationId, long quotaBytes, Guid? storageAddonId = null)
    {
        host.BillingSubscriptionClientMock
            .Setup(x => x.GetActiveSubscriptionAsync(organizationId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveSubscriptionInfo
            {
                Code = "test",
                IsPersonal = true,
                StorageQuotaBytes = quotaBytes,
                StorageAddonId = storageAddonId,
                IncludedTokensCount = 2_500_000,
                LimitPeriodStartedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            });
    }
}

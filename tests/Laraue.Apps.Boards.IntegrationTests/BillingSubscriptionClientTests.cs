using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Laraue.Apps.Billing.Internal.Contracts;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Billing;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// The real <see cref="BillingSubscriptionClient"/> against a mocked gRPC client: every other test mocks
/// <see cref="IBillingSubscriptionClient"/> itself, so which Billing subscription an organization is
/// resolved to is covered only here.
/// </summary>
[Collection("IntegrationTest")]
public class BillingSubscriptionClientTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private static readonly DateTime PeriodStartedAt = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEndsAt = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetActiveSubscriptionAsync_ShouldUseThePersonalSubscriptionOfTheUser_WhenOrganizationIsPersonal()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId);

        var billing = new Mock<SubscriptionService.SubscriptionServiceClient>();
        billing
            .Setup(x => x.GetActivePersonalSubscriptionAsync(
                It.IsAny<GetActivePersonalSubscriptionRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(GrpcTestHelpers.AsyncUnaryCallOf(new ActiveSubscriptionResponse
            {
                Code = "Free",
                LaraueBoardsPersonal = new LaraueBoardsPersonalSubscription
                {
                    LimitIssuesPerMonth = 500,
                    IncludedTokensCount = 25_000,
                },
                LimitPeriodStartedAt = Timestamp.FromDateTime(PeriodStartedAt),
                PeriodEndsAt = Timestamp.FromDateTime(PeriodEndsAt),
                PeriodResets = true,
            }));

        var client = new BillingSubscriptionClient(testScope.Database, billing.Object);

        var subscription = await client.GetActiveSubscriptionAsync(organization.Id, userId, CancellationToken.None);

        // The personal plan is the user's own: it is looked up by the user id, never by an organization id.
        billing.Verify(
            x => x.GetActivePersonalSubscriptionAsync(
                It.Is<GetActivePersonalSubscriptionRequest>(r => r.UserId == userId.ToString()),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        billing.Verify(
            x => x.GetActiveOrganizationSubscriptionAsync(
                It.IsAny<GetActiveOrganizationSubscriptionRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.True(subscription.IsPersonal);
        Assert.Equal(500, subscription.LimitIssuesPerMonth);
        Assert.Equal(PeriodStartedAt, subscription.LimitPeriodStartedAt);
        Assert.Equal(PeriodEndsAt, subscription.PeriodEndsAt);
        Assert.True(subscription.PeriodResets);
    }

    [Fact]
    public async Task GetActiveSubscriptionAsync_ShouldUseTheSubscriptionOfTheOrganization_WhenOrganizationIsATeam()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        // A team organization is billed under its own id, which Billing knows it by.
        var billingId = await testScope.Database.Organizations
            .Where(x => x.Id == organization.Id)
            .Select(x => x.BillingId!.Value)
            .SingleAsync();

        var billing = new Mock<SubscriptionService.SubscriptionServiceClient>();
        billing
            .Setup(x => x.GetActiveOrganizationSubscriptionAsync(
                It.IsAny<GetActiveOrganizationSubscriptionRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(GrpcTestHelpers.AsyncUnaryCallOf(new ActiveSubscriptionResponse
            {
                Code = "Team",
                LaraueBoardsTeam = new LaraueBoardsTeamSubscription
                {
                    LimitIssuesPerMonth = 50_000,
                    IncludedTokensCount = 750_000,
                },
                LimitPeriodStartedAt = Timestamp.FromDateTime(PeriodStartedAt),
            }));

        var client = new BillingSubscriptionClient(testScope.Database, billing.Object);

        var subscription = await client.GetActiveSubscriptionAsync(organization.Id, userId, CancellationToken.None);

        billing.Verify(
            x => x.GetActiveOrganizationSubscriptionAsync(
                It.Is<GetActiveOrganizationSubscriptionRequest>(r => r.OrganizationId == billingId.ToString()),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.False(subscription.IsPersonal);
        Assert.Equal(PeriodStartedAt, subscription.LimitPeriodStartedAt);
        // A paid plan without a reported end has none.
        Assert.Null(subscription.PeriodEndsAt);
        Assert.False(subscription.PeriodResets);
    }
}

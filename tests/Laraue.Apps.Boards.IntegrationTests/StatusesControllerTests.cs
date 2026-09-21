using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class StatusesControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<StatusesController> _statusesController = host.Controller<StatusesController>();

    [Fact]
    public async Task User_ShouldSoftDeleteStatusAndItsIssues_WhenStatusIsDeleted()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o.AddSpace(userId, space => space
                .AddEpic(userId, epic => epic
                    .AddStatus(s => s.WithName("In Progress"))
                    .AddIssue(userId, 1, issue => issue.WithContent("Doomed issue")))));

        var status = organization.GetStatus(1, 1, 1);
        var issueData = organization.GetIssueData(1, 1, 1, 0);

        await _statusesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Delete(status.Id, CancellationToken.None));

        var deletedStatus = await testScope.Database.Statuses.SingleAsyncEF(x => x.Id == status.Id);
        Assert.NotNull(deletedStatus.DeletedAt);
        Assert.Equal(userId, deletedStatus.DeletedByUserId);

        var deletedIssue = await testScope.Database.Issues.SingleAsyncEF(x => x.Id == issueData.Issue.Id);
        Assert.NotNull(deletedIssue.DeletedAt);
    }

    [Fact]
    public async Task User_ShouldNotDeleteStatus_WhenItIsTheOnlyStatusInEpic()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        // The organization's own default space/epic (index 0) is seeded with exactly one status.
        var organization = await testScope.InitializeOrganization(userId);

        var status = organization.GetStatus(0, 0, 0);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => _statusesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Delete(status.Id, CancellationToken.None)));

        ex.HasInnerException<BadRequestException>();

        var untouchedStatus = await testScope.Database.Statuses.SingleAsyncEF(x => x.Id == status.Id);
        Assert.Null(untouchedStatus.DeletedAt);
    }
}

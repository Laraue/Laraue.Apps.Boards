using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class StatusesControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<StatusesController> _statusesController = host.Controller<StatusesController>();

    [Fact]
    public async Task User_ShouldCreateStatusWithCategory_WhenCategoryGiven()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        var statusId = await _statusesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.CreateStatus(
                new CreateStatusRequest
                {
                    Name = "Done",
                    Color = "#111111",
                    EpicId = organization.GetEpic(0, 0).Id,
                    Category = StatusCategory.Completed,
                },
                CancellationToken.None));

        var status = await testScope.Database.Statuses.SingleAsyncEF(x => x.Id == statusId);
        Assert.Equal(StatusCategory.Completed, status.Category);
    }

    [Fact]
    public async Task User_ShouldChangeStatusCategory_WhenStatusIsEdited()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);

        var status = organization.GetStatus(0, 0, 0);

        await _statusesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Edit(
                status.Id,
                new EditStatusRequest
                {
                    Name = "Done",
                    Color = "#111111",
                    Category = StatusCategory.Completed,
                },
                CancellationToken.None));

        var editedStatus = await testScope.Database.Statuses
            .AsNoTracking()
            .SingleAsyncEF(x => x.Id == status.Id);
        Assert.Equal("Done", editedStatus.Name);
        Assert.Equal(StatusCategory.Completed, editedStatus.Category);
    }

    [Fact]
    public async Task User_ShouldGetStatusCategories_WhenListingStatuses()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(
            userId,
            o => o.AddSpace(userId, space => space
                .AddEpic(userId, epic => epic
                    .AddStatus(s => s.WithName("Done").WithCategory(StatusCategory.Completed)))));

        var statuses = await _statusesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetStatuses(
                new GetStatusesRequest { EpicId = organization.GetEpic(1, 1).Id },
                CancellationToken.None));

        Assert.NotNull(statuses);
        Assert.Equal(StatusCategory.Created, Assert.Single(statuses, s => s.Name == "New").Category);
        Assert.Equal(StatusCategory.Completed, Assert.Single(statuses, s => s.Name == "Done").Category);
    }

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

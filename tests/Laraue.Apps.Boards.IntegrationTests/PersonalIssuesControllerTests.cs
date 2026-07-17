using Laraue.Apps.Boards.DataAccess.Enums;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Sorting;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class PersonalIssuesControllerTests(WebApiTestHost host)  : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<IssuesController> _issuesController = host.Controller<IssuesController>();
    
    [Fact]
    public async Task User_ShouldCreatePersonalIssue_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddTextAttribute("Note")
                .AddListAttribute("Type", ["Bug", "Feature"]));

        var space = organization.Spaces![0];
        var backlog = space.Epics![0];
        var defaultStatus = backlog.Statuses![0];
        
        var noteAttribute = organization.GetAttribute(0);
        var typeAttribute = organization.GetAttribute(1);
        
        var request = new CreateIssueRequest
        {
            Content = "New Issue",
            StatusId = defaultStatus.Id,
            AttributeValues = new Dictionary<long, string>
            {
                [noteAttribute.Id] = "My note",
                [typeAttribute.Id] = typeAttribute.GetListValue(1).Id.ToString(), // Set ID of 'Feature' value
            },
            AssigneeId = userId,
        };
        var issueId = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(request));

        var issue = await testScope.Database.Issues.FirstAsyncEF(e => e.Id == issueId);
        
        Assert.Equal("New Issue", issue.Content);
        Assert.Equal(defaultStatus.Id, issue.StatusId);
        Assert.NotEqual(default, issue.CreatedAt);
        Assert.NotEqual(default, issue.UpdatedAt);
        Assert.Equal(userId, issue.OwnerId);
        
        var textAttribute = await testScope.Database.IssueAttributeTextValues.SingleAsyncEF();
        Assert.Equal(issue.Id, textAttribute.IssueId);
        Assert.Equal(noteAttribute.Id, textAttribute.AttributeId);
        Assert.Equal("My note", textAttribute.Text);
        
        var listAttribute = await testScope.Database.IssueAttributeListValues.SingleAsyncEF();
        Assert.Equal(issue.Id, listAttribute.IssueId);
        Assert.Equal(typeAttribute.Id, listAttribute.AttributeId);
        Assert.Equal(typeAttribute.GetListValue(1).Id, listAttribute.AttributeListValueId); // ID of 'Feature' value
    }
    
    [Fact]
    public async Task Issue_ShouldHasCorrectCounter_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(userId);

        var space = organization.Spaces![0];
        var backlog = space.Epics![0];
        var defaultStatus = backlog.Statuses![0];
        
        // First issue has number 1
        var issueId = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(
                new CreateIssueRequest
                {
                    Content = "New Issue",
                    StatusId = defaultStatus.Id,
                    AssigneeId = userId,
                }));

        var issueNumber = await testScope.Database.IssueNumbers.FirstAsyncEF(e => e.IssueId == issueId);
        Assert.Equal(1, issueNumber.Number);
        
        // Second issue has number 2
        issueId = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Create(
                new CreateIssueRequest
                {
                    Content = "New Issue",
                    StatusId = defaultStatus.Id,
                    AssigneeId = userId,
                }));

        issueNumber = await testScope.Database.IssueNumbers.FirstAsyncEF(e => e.IssueId == issueId);
        Assert.Equal(2, issueNumber.Number);
    }
    
    [Fact]
    public async Task User_ShouldUpdatePersonalIssue_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddTextAttribute("Note")
                .AddListAttribute("Type", ["Bug", "Feature"])
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, i => i
                            .WithContent("Hi")))));

        var issue = organization.GetIssue(1, 1, 0, 0);
        var noteAttribute = organization.GetAttribute(0);
        var typeAttribute = organization.GetAttribute(1);
        
        var request = new UpdateIssueRequest
        {
            Content = "New",
            AttributeValues = new Dictionary<long, string>
            {
                [noteAttribute.Id] = "My note",
                [typeAttribute.Id] = typeAttribute.GetListValue(1).Id.ToString(), // Set ID of 'Feature' value
            },
            AssigneeId = userId,
        };
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Update(issue.Id, request));

        issue = await testScope.Database.Issues.FirstAsyncEF(e => e.Id == issue.Id);
        
        Assert.True(issue.CreatedAt < issue.UpdatedAt);
        Assert.Equal("New", issue.Content);

        var textAttribute = await testScope.Database.IssueAttributeTextValues.SingleAsyncEF();
        Assert.Equal(issue.Id, textAttribute.IssueId);
        Assert.Equal(noteAttribute.Id, textAttribute.AttributeId);
        Assert.Equal("My note", textAttribute.Text);
        
        var listAttribute = await testScope.Database.IssueAttributeListValues.SingleAsyncEF();
        Assert.Equal(issue.Id, listAttribute.IssueId);
        Assert.Equal(typeAttribute.Id, listAttribute.AttributeId);
        Assert.Equal(typeAttribute.GetListValue(1).Id, listAttribute.AttributeListValueId); // ID of 'Feature' value
    }
    
    [Fact]
    public async Task User_ShouldDeletePersonalIssue_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0))));

        var issue = organization.GetIssue(1, 1, 0, 0);
        
        await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Delete(issue.Id));

        issue = await testScope.Database.Issues.FirstOrDefaultAsyncEF(e => e.Id == issue.Id);
        Assert.Null(issue);
    }
    
    [Fact]
    public async Task User_ShouldGetPersonalIssue_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u => { u.TelegramUserName = "snake1977"; });
        var timestamp = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .WithName("Top Epic")
                        .WithColor("#121212")
                        .AddStatus(st => st
                            .WithName("Beautiful status")
                            .WithColor("#212121"))
                        .AddIssue(userId, 1, issue => issue
                            .WithContent("Hi")
                            .WithTimestamp(timestamp)))));

        var issue = organization.GetIssue(1, 1, 1, 0);
        
        var issueDto = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssue(issue.Id));
        
        Assert.NotNull(issueDto);
        Assert.Equal("Hi", issueDto.Content);
        Assert.Equal("Top Epic", issueDto.EpicName);
        Assert.Equal("#121212", issueDto.EpicColor);
        Assert.Equal("Beautiful status", issueDto.StatusName);
        Assert.Equal("#212121", issueDto.StatusColor);
        Assert.Equal("sn", issueDto.SenderInitial);
        Assert.Equal("snake1977", issueDto.Sender);
        Assert.Equal(timestamp, issueDto.Time);
        Assert.Equal(timestamp, issueDto.UpdatedAt);
    }
    
    [Fact]
    public async Task User_ShouldGetPersonalIssues_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u => { u.TelegramUserName = "snake1977"; });
        var timestamp = new DateTime(2020, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Hi")
                            .WithTimestamp(timestamp)))));

        var status = organization.GetStatus(1, 1, 0);
        var epic = organization.GetEpic(1, 1);
        
        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssuesByStatus(
                status.Id,
                new GetIssuesRequest
                {
                    Skip = 0,
                    Take = 10,
                }));
        
        Assert.NotNull(issuesResult);
        var issueDto = Assert.Single(issuesResult.Data);
        Assert.Equal("Hi", issueDto.Content);
        Assert.Equal("sn", issueDto.AssigneeInitial);
        Assert.Equal("snake1977", issueDto.Sender);
        Assert.Equal(status.Id, issueDto.StatusId);
        Assert.Equal(timestamp, issueDto.Time);
        Assert.Equal(epic.Id, issueDto.EpicId);
        Assert.Empty(issueDto.Media);
    }
    
    [Fact]
    public async Task User_ShouldSearchPersonalIssues_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u => { u.TelegramUserName = "snake1977"; });
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("Hi"))
                        .AddIssue(userId, 0, issue => issue.WithContent("John")))));

        var status = organization.GetStatus(1, 1, 0);
        
        var issuesResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetIssuesByStatus(
                status.Id,
                new GetIssuesRequest
                {
                    Skip = 0,
                    Take = 10,
                    SearchString = "jo"
                }));
        
        Assert.NotNull(issuesResult);
        var issueDto = Assert.Single(issuesResult.Data);
        Assert.Equal("John", issueDto.Content);
    }
    
    [Fact]
    public async Task User_ShouldGetBoard_Always()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u => { u.TelegramUserName = "snake1977"; });
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddStatus(st => st.WithName("Done"))
                        .AddIssue(userId, 1, issue => issue.WithContent("Build app"))
                        .AddIssue(userId, 0, issue => issue.WithContent("Deliver app"))
                        .AddIssue(userId, 0, issue => issue.WithContent("Fix bug")))));
        
        var epic =  organization.GetEpic(1, 1);
        var backlogStatus = organization.GetStatus(1, 1, 0);
        var doneStatus = organization.GetStatus(1, 1, 1);

        var boardColumns = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.GetBoard(
                new GetBoardRequest
                {
                    Take = 10,
                    SearchString = "app",
                    EpicId = epic.Id,
                }));
        
        Assert.NotNull(boardColumns);
        Assert.Equal(2, boardColumns.Length);
        
        var backlogColumn = boardColumns[0];
        Assert.Equal(backlogStatus.Id, backlogColumn.StatusId);
        Assert.Equal(1, backlogColumn.Items.TotalCount);
        Assert.Equal(1, backlogColumn.Items.Offset);
        Assert.False(backlogColumn.Items.HasNext);
        var backlogIssue = Assert.Single(backlogColumn.Items.Data);
        Assert.Equal("Deliver app", backlogIssue.Content);
        Assert.Equal("sn", backlogIssue.AssigneeInitial);
        Assert.Equal("snake1977", backlogIssue.Sender);
        Assert.Equal(backlogStatus.Id, backlogIssue.StatusId);
        Assert.Equal(epic.Id, backlogIssue.EpicId);
        Assert.Empty(backlogIssue.Media);
        
        var doneColumn = boardColumns[1];
        Assert.Equal(doneStatus.Id, doneColumn.StatusId);
        Assert.Equal(1, doneColumn.Items.TotalCount);
        Assert.Equal(1, doneColumn.Items.Offset);
        Assert.False(doneColumn.Items.HasNext);
        var doneIssue = Assert.Single(doneColumn.Items.Data);
        Assert.Equal("Build app", doneIssue.Content);
    }
    
    [Fact]
    public async Task User_ShouldSearchIssues_WhenFilterByEpicId()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser(u => { u.TelegramUserName = "snake1977"; });
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("Build app"))
                        .AddIssue(userId, 0, issue => issue.WithContent("Deliver app"))
                        .AddIssue(userId, 0, issue => issue.WithContent("Fix bug")))));
        
        var epic = organization.GetEpic(1, 1);
        var backlogStatus = organization.GetStatus(1, 1, 0);

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    SearchString = "build",
                    Page = 0,
                    PerPage = 10,
                    EpicIds = new [] { epic.Id },
                }));
        
        Assert.NotNull(searchResult);
        var item = Assert.Single(searchResult.Data);
        
        Assert.Equal("Build app", item.Content);
        Assert.Equal("sn", item.AssigneeInitial);
        Assert.Equal("snake1977", item.Sender);
        Assert.Equal(backlogStatus.Id, item.StatusId);
        Assert.Equal(epic.Id, item.EpicId);
        Assert.Empty(item.Media);
    }
    
    [Fact]
    public async Task User_ShouldSearchIssues_WhenFilterBySpaceId()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, "SP1", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("Other space app"))))
                .AddSpace(userId, "SP2", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("Build app"))
                        .AddIssue(userId, 0, issue => issue.WithContent("Deliver app")))));
        
        var space = organization.GetSpace(2);

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    SearchString = "app",
                    Page = 0,
                    PerPage = 10,
                    SpaceIds = new [] { space.Id },
                }));
        
        Assert.NotNull(searchResult);
        Assert.Equal(2, searchResult.Data.Count);
    }
    
    [Fact]
    public async Task User_ShouldSearchIssues_WhenNoFilterBySpaceOrEpic()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, "SP1", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("Other space app"))))
                .AddSpace(userId, "SP2", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue.WithContent("Build app"))
                        .AddIssue(userId, 0, issue => issue.WithContent("Deliver app"))
                        .AddIssue(userId, 0, issue => issue.WithContent("Fix bug")))));

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    SearchString = "app",
                    Page = 0,
                    PerPage = 10,
                }));
        
        Assert.NotNull(searchResult);
        Assert.Equal(3, searchResult.Data.Count);
    }
    
    [Fact]
    public async Task User_ShouldSearchIssues_WhenFilterByCustomListAttribute()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddListAttribute("Color", ["Red", "Green"])
                .AddSpace(userId, "SP2", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Build app")
                            .WithAttributeValue(0, 0)) // Color is 'Red'
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Deliver app")
                            .WithAttributeValue(0, 1))))); // Color is 'Green'
        
        var redAttribute = organization.GetAttribute(0);
        var searchFilters = new Dictionary<long, AttributeFilterValue>
        {
            [redAttribute.Id] = new EnumAttributeFilterValue // Search by 'Color' = 'Red'
            {
                Ids = [redAttribute.GetListValue(0).Id]
            }
        };

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Filters = searchFilters
                }));
        
        Assert.NotNull(searchResult);
        var result = Assert.Single(searchResult.Data);
        Assert.Equal("Build app", result.Content);
    }
    
    [Fact]
    public async Task User_ShouldSearchIssues_WhenFilterByCustomTextAttribute()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddTextAttribute("Jira Issue Number")
                .AddSpace(userId, "SP2", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Build app")
                            .WithAttributeValue(0, "14432"))
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Deliver app")
                            .WithAttributeValue(0, "53312")))));
        
        var issueNumberAttribute = organization.GetAttribute(0);
        var searchFilters = new Dictionary<long, AttributeFilterValue>
        {
            [issueNumberAttribute.Id] = new StringAttributeFilterValue
            {
                SearchString = "5331"
            }
        };

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Filters = searchFilters
                }));
        
        Assert.NotNull(searchResult);
        var result = Assert.Single(searchResult.Data);
        Assert.Equal("Deliver app", result.Content);
    }
    
    [Theory]
    [InlineData(SortingDirection.Ascending)]
    [InlineData(SortingDirection.Descending)]
    public async Task User_ShouldSearchIssues_WhenSortingByEnumProperty(SortingDirection direction)
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddListAttribute("Color", ["Red", "Green"])
                .AddSpace(userId, "SP2", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Build app")
                            .WithAttributeValue(0, 0)) // Color is 'Red'
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Deliver app")
                            .WithAttributeValue(0, 1))))); // Color is 'Green'
        
        var redAttribute = organization.GetAttribute(0);

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Sorting = new ByAttributeIssueSorting
                    {
                        AttributeId = redAttribute.Id,
                        Direction = direction,
                    }
                }));
        
        Assert.NotNull(searchResult);
        string[] excepted = direction == SortingDirection.Ascending
            ? ["Build app", "Deliver app"]
            : ["Deliver app", "Build app"];
        Assert.Equal(excepted, searchResult.Data.Select(x => x.Content));
    }
    
    [Theory]
    [InlineData(SortingDirection.Ascending)]
    [InlineData(SortingDirection.Descending)]
    public async Task User_ShouldSearchIssues_WhenSortingByCreatedAt(SortingDirection direction)
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, "SP2", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Build app"))
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Deliver app")))));

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10,
                    Sorting = new ByPropertyIssueSorting
                    {
                        Property = IssueProperty.CreatedAt,
                        Direction = direction,
                    }
                }));
        
        Assert.NotNull(searchResult);
        string[] excepted = direction == SortingDirection.Ascending
            ? ["Build app", "Deliver app"]
            : ["Deliver app", "Build app"];
        Assert.Equal(excepted, searchResult.Data.Select(x => x.Content));
    }
    
    [Fact]
    public async Task User_ShouldSearchIssues_WhenSortingIsNotSet()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializePersonalOrganization(
            userId,
            o => o
                .AddSpace(userId, "SP2", s => s
                    .AddEpic(userId, e => e
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Build app"))
                        .AddIssue(userId, 0, issue => issue
                            .WithContent("Deliver app")))));

        var searchResult = await _issuesController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Search(
                new SearchRequest
                {
                    Page = 0,
                    PerPage = 10
                }));
        
        Assert.NotNull(searchResult);
        Assert.Equal(["Deliver app", "Build app"], searchResult.Data.Select(x => x.Content));
    }
}

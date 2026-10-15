using System.Net;
using Grpc.Core;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.WebApiHost.Controllers;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions.Web;
using Microsoft.EntityFrameworkCore;
using Moq;
using Contracts = Laraue.Apps.Feedback.Internal.Contracts;

namespace Laraue.Apps.Boards.IntegrationTests;

[Collection("IntegrationTest")]
public class FeedbackControllerTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    private readonly Proxy<FeedbackController> _feedbackController = host.Controller<FeedbackController>();

    [Fact]
    public async Task Send_ShouldSubmitFeedbackWithGlobalUserId_WhenUserIsSignedIn()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var globalUserId = await GetGlobalUserId(testScope, userId);
        var submitted = SetupSubmit(42);
        var feedback = new SendFeedbackRequest
        {
            Type = FeedbackType.Idea,
            Message = "Dark theme, please",
            Context = new Dictionary<string, string>
            {
                ["page"] = "https://boards.example.com/spaces/UNC",
                ["locale"] = "en",
            },
        };

        var response = await _feedbackController
            .WithUserAuthorization(userId)
            .Execute(x => x.Send(feedback, default));

        Assert.Equal(42, response!.FeedbackId);
        var request = Assert.Single(submitted);
        Assert.Equal(globalUserId.ToString(), request.UserId);
        Assert.Equal(Contracts.FeedbackType.Idea, request.Type);
        Assert.Equal("Dark theme, please", request.Message);
        Assert.Equal("https://boards.example.com/spaces/UNC", request.Context["page"]);
        Assert.Equal("en", request.Context["locale"]);
        Assert.False(request.Context.ContainsKey(UserFeedbackService.OrganizationContextKey));
    }

    [Fact]
    public async Task Send_ShouldAddCurrentOrganization_WhenRequestHasOrganizationToken()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(userId);
        var submitted = SetupSubmit(1);

        await _feedbackController
            .WithOrganizationAuthorization(organization.Id, userId)
            .Execute(x => x.Send(
                new SendFeedbackRequest { Type = FeedbackType.Bug, Message = "Cards don't move" },
                default));

        var request = Assert.Single(submitted);
        Assert.Equal(organization.Id.ToString(), request.Context[UserFeedbackService.OrganizationContextKey]);
    }

    [Fact]
    public async Task Send_ShouldDropOrganizationFromFrontend_WhenRequestHasNoOrganizationToken()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var submitted = SetupSubmit(1);
        var feedback = new SendFeedbackRequest
        {
            Type = FeedbackType.Question,
            Message = "How do I invite a member?",
            Context = new Dictionary<string, string>
            {
                [UserFeedbackService.OrganizationContextKey] = "999",
            },
        };

        await _feedbackController
            .WithUserAuthorization(userId)
            .Execute(x => x.Send(feedback, default));

        var request = Assert.Single(submitted);
        Assert.False(request.Context.ContainsKey(UserFeedbackService.OrganizationContextKey));
    }

    [Fact]
    public async Task Send_ShouldSubmitTrimmedContact_WhenUserGaveOne()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var submitted = SetupSubmit(1);

        await _feedbackController
            .WithUserAuthorization(userId)
            .Execute(x => x.Send(
                new SendFeedbackRequest { Type = FeedbackType.Question, Message = "Call me", Contact = " ada@example.com " },
                default));

        var request = Assert.Single(submitted);
        Assert.Equal("ada@example.com", request.Contact);
    }

    [Fact]
    public async Task Send_ShouldLeaveContactUnset_WhenContactIsBlank()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var submitted = SetupSubmit(1);

        await _feedbackController
            .WithUserAuthorization(userId)
            .Execute(x => x.Send(
                new SendFeedbackRequest { Type = FeedbackType.Idea, Message = "No contact", Contact = "  " },
                default));

        var request = Assert.Single(submitted);
        Assert.False(request.HasContact);
    }

    [Fact]
    public async Task Send_ShouldReturnBadRequest_WhenTypeIsMissing()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        var submitted = SetupSubmit(1);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _feedbackController
            .WithUserAuthorization(userId)
            .Execute(x => x.Send(
                new SendFeedbackRequest { Message = "No type" },
                default)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Empty(submitted);
    }

    /// <summary>
    /// The mock stands in for the whole client pipeline, so it throws what Laraue.Grpc.Client turns
    /// Feedback's INVALID_ARGUMENT into (e.g. a message over 3000 characters).
    /// </summary>
    [Fact]
    public async Task Send_ShouldReturnBadRequest_WhenFeedbackRejectsRequest()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        SetupSubmitThrows(new BadRequestException(string.Empty, "message is too long"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _feedbackController
            .WithUserAuthorization(userId)
            .Execute(x => x.Send(
                new SendFeedbackRequest { Type = FeedbackType.Bug, Message = new string('a', 3001) },
                default)));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Send_ShouldReturnServiceUnavailable_WhenFeedbackIsUnavailable()
    {
        using var testScope = host.CreateTestScope();
        var userId = await testScope.CreateUser();
        SetupSubmitThrows(new RpcException(new Status(StatusCode.Unavailable, "Feedback is down")));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _feedbackController
            .WithUserAuthorization(userId)
            .Execute(x => x.Send(
                new SendFeedbackRequest { Type = FeedbackType.Bug, Message = "Something broke" },
                default)));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task Send_ShouldReturnUnauthorized_WhenRequestHasNoToken()
    {
        var submitted = SetupSubmit(1);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => _feedbackController
            .Execute(x => x.Send(
                new SendFeedbackRequest { Type = FeedbackType.Bug, Message = "Anonymous" },
                default)));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Empty(submitted);
    }

    private List<Contracts.SubmitFeedbackRequest> SetupSubmit(long feedbackId)
    {
        var submitted = new List<Contracts.SubmitFeedbackRequest>();
        host.FeedbackClientMock
            .Setup(x => x.SubmitFeedbackAsync(
                It.IsAny<Contracts.SubmitFeedbackRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Callback((Contracts.SubmitFeedbackRequest request, Metadata? _, DateTime? _, CancellationToken _) =>
                submitted.Add(request))
            .Returns(GrpcTestHelpers.AsyncUnaryCallOf(new Contracts.SubmitFeedbackResponse { FeedbackId = feedbackId }));

        return submitted;
    }

    private void SetupSubmitThrows(Exception exception)
    {
        host.FeedbackClientMock
            .Setup(x => x.SubmitFeedbackAsync(
                It.IsAny<Contracts.SubmitFeedbackRequest>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Throws(exception);
    }

    private static Task<Guid> GetGlobalUserId(WebApiTestHostScope testScope, Guid userId)
    {
        return testScope.Database.Users
            .Where(x => x.Id == userId)
            .Select(x => x.GlobalUserId)
            .SingleAsync();
    }
}

using Grpc.Core;
using Laraue.Apps.Boards.DataAccess.Models;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.McpHost.Services;
using Laraue.Apps.Boards.WebApiServices.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Laraue.Apps.Boards.IntegrationTests;

/// <summary>
/// Uploads' references in Laraue.Apps.Storage are confirmed and deleted through the outbox, after Boards' commit.
/// Assertions read the fake Storage client after <see cref="StorageReferenceOperationsSender.RunOnceAsync"/>, so the
/// sender running on its own timer in the background doesn't change the outcome.
/// </summary>
[Collection("IntegrationTest")]
public class StorageReferenceOperationsTests(WebApiTestHost host) : IClassFixture<WebApiTestHost>
{
    [Fact]
    public async Task Sender_ShouldConfirmUpload_WhenIssueWithFileIsCreated()
    {
        using var testScope = host.CreateTestScope();
        var (mcpService, authData, statusId) = await Setup(testScope);

        var issueKey = await mcpService.CreateIssue(authData, "Content", "Title", statusId, null, null, [NotesFile()], CancellationToken.None);
        await RunSender();

        var referenceId = await ReferenceOfOnlyAttachment(testScope, mcpService, authData, issueKey);
        Assert.True(host.StorageClient.References[referenceId].Confirmed);
        Assert.False(await testScope.Database.StorageReferenceOperations.AnyAsync(x => x.ReferenceId == referenceId));
    }

    [Fact]
    public async Task Sender_ShouldDeleteReference_WhenAttachmentIsRemoved()
    {
        using var testScope = host.CreateTestScope();
        var (mcpService, authData, statusId) = await Setup(testScope);
        var issueKey = await mcpService.CreateIssue(authData, "Content", "Title", statusId, null, null, [NotesFile()], CancellationToken.None);
        var detail = await mcpService.GetIssue(authData, issueKey, CancellationToken.None);
        var referenceId = await ReferenceOfOnlyAttachment(testScope, mcpService, authData, issueKey);

        await mcpService.EditIssue(authData, issueKey, "Content", "Title", null, null, null, [Assert.Single(detail.Attachments).Id], CancellationToken.None);
        await RunSender();

        Assert.False(host.StorageClient.References.ContainsKey(referenceId));
    }

    [Fact]
    public async Task Sender_ShouldDeleteReference_WhenCommentWithFileIsDeleted()
    {
        using var testScope = host.CreateTestScope();
        var (mcpService, authData, statusId) = await Setup(testScope);
        var issueKey = await mcpService.CreateIssue(authData, "Content", "Title", statusId, null, null, null, CancellationToken.None);
        var commentId = await mcpService.CreateComment(authData, issueKey, "See the notes", [NotesFile()], CancellationToken.None);
        var referenceId = await testScope.Database.IssueCommentsAttachments
            .Where(x => x.CommentId == commentId)
            .Select(x => x.Attachment!.File!.StorageFile!.ReferenceId!.Value)
            .SingleAsync();

        await mcpService.DeleteComment(authData, commentId, CancellationToken.None);
        await RunSender();

        Assert.False(host.StorageClient.References.ContainsKey(referenceId));
    }

    [Fact]
    public async Task Sender_ShouldRetryLater_WhenStorageFails()
    {
        using var testScope = host.CreateTestScope();
        var referenceId = Guid.NewGuid();
        host.StorageClient.References[referenceId] = new(Guid.NewGuid(), Confirmed: false);
        await Queue(testScope, referenceId, StorageReferenceOperationKind.Confirm);
        host.StorageClient.NextReferenceCallFailure = new RpcException(new global::Grpc.Core.Status(StatusCode.Unavailable, "Storage is down"));

        await RunSender();

        var operation = await testScope.Database.StorageReferenceOperations.SingleAsync(x => x.ReferenceId == referenceId);
        Assert.Equal(1, operation.Attempts);
        Assert.Contains("Storage is down", operation.LastError);
        Assert.True(operation.NextAttemptAt > DateTime.UtcNow);
        Assert.False(host.StorageClient.References[referenceId].Confirmed);
    }

    [Fact]
    public async Task Sender_ShouldDropConfirm_WhenReferenceIsAlreadyGone()
    {
        using var testScope = host.CreateTestScope();
        var referenceId = Guid.NewGuid();
        await Queue(testScope, referenceId, StorageReferenceOperationKind.Confirm);

        await RunSender();

        Assert.False(await testScope.Database.StorageReferenceOperations.AnyAsync(x => x.ReferenceId == referenceId));
    }

    private Task RunSender()
    {
        return host.Services.GetRequiredService<StorageReferenceOperationsSender>().RunOnceAsync(CancellationToken.None);
    }

    private static async Task Queue(WebApiTestHostScope testScope, Guid referenceId, StorageReferenceOperationKind kind)
    {
        testScope.Database.StorageReferenceOperations.Add(new StorageReferenceOperation
        {
            ReferenceId = referenceId,
            Kind = kind,
            CreatedAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow,
        });
        await testScope.Database.SaveChangesAsync();
    }

    private static async Task<(IIssueMcpService, Laraue.Apps.Boards.Common.OrganizationAuthData, long)> Setup(WebApiTestHostScope testScope)
    {
        var ownerId = await testScope.CreateUser();
        var organization = await testScope.InitializeOrganization(ownerId, org => { });
        var statusId = organization.GetStatus(0, 0, 0).Id;

        return (IssueMcpServiceTests.CreateIssueMcpService(testScope), IssueMcpServiceTests.AuthDataFor(organization.Id, ownerId), statusId);
    }

    private static async Task<Guid> ReferenceOfOnlyAttachment(
        WebApiTestHostScope testScope,
        IIssueMcpService mcpService,
        Laraue.Apps.Boards.Common.OrganizationAuthData authData,
        string issueKey)
    {
        var detail = await mcpService.GetIssue(authData, issueKey, CancellationToken.None);
        var attachmentId = Assert.Single(detail.Attachments).Id;

        return await testScope.Database.Attachments
            .Where(x => x.Id == attachmentId)
            .Select(x => x.File!.StorageFile!.ReferenceId!.Value)
            .SingleAsync();
    }

    private static FileAttachment NotesFile() => new("notes.txt", "text/plain", Convert.ToBase64String("notes"u8.ToArray()));
}

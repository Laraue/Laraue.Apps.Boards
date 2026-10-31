using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services.Metrics;
using Laraue.Core.DataAccess.EFCore.Extensions;
using Laraue.Core.DateTime.Services.Abstractions;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// What the user picked in the organization setup window (BRD-293).
/// </summary>
public enum OrganizationSetupTemplate
{
    /// <summary>
    /// Nothing is added.
    /// </summary>
    Empty,

    /// <summary>
    /// A sample space with a board and a few issues, see <see cref="SampleProjectContent"/>.
    /// </summary>
    Sample,
}

public interface ICoreOrganizationSetupService
{
    /// <summary>
    /// Marks the organization as set up and adds what <paramref name="template"/> says. False, with nothing
    /// changed, when it was already set up - so finishing the window twice adds one sample project. Requires a
    /// transaction.
    /// </summary>
    Task<bool> Complete(
        long organizationId,
        Guid userId,
        OrganizationSetupTemplate template,
        CancellationToken cancellationToken);
}

public class CoreOrganizationSetupService(
    DatabaseContext context,
    ICoreSpacesService coreSpacesService,
    ICoreEpicsService coreEpicsService,
    ICoreIssuesService coreIssuesService,
    ICoreIssueLinksService coreIssueLinksService,
    IDateTimeProvider dateTimeProvider,
    BoardsMetrics metrics)
    : ICoreOrganizationSetupService
{
    public async Task<bool> Complete(
        long organizationId,
        Guid userId,
        OrganizationSetupTemplate template,
        CancellationToken cancellationToken)
    {
        context.Database.EnsureTransactionStarted();

        // Conditional, so of two concurrent calls only one sees a row updated.
        var updatedCount = await context.ActiveOrganizations()
            .Where(x => x.Id == organizationId && x.SetupCompletedAt == null)
            .ExecuteUpdateAsync(
                u => u.SetProperty(x => x.SetupCompletedAt, dateTimeProvider.UtcNow),
                cancellationToken);

        if (updatedCount == 0)
            return false;

        if (template == OrganizationSetupTemplate.Sample)
            await AddSampleProject(organizationId, userId, cancellationToken);

        metrics.RecordOrganizationSetupCompleted(template);

        return true;
    }

    private async Task AddSampleProject(long organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var usedKeys = await context.ActiveSpaces()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => x.Key)
            .ToArrayAsyncEF(cancellationToken);

        var key = SampleProjectContent.SpaceKeys
            .FirstOrDefault(x => !usedKeys.Contains(x));

        // Every sample key is taken by the user's own spaces: they don't need the sample that much.
        if (key is null)
            return;

        await coreSpacesService.Create(
            organizationId,
            userId,
            key,
            SampleProjectContent.SpaceName,
            Palette.RandomColor(),
            cancellationToken);

        var spaceId = await coreSpacesService.GetSpaceIdBySpaceKey(organizationId, key, cancellationToken);

        await coreSpacesService.Update(
            spaceId,
            setters => setters.SetProperty(x => x.Description, SampleProjectContent.SpaceDescription),
            cancellationToken);

        var epicId = await coreEpicsService.Create(
            spaceId,
            userId,
            SampleProjectContent.BoardName,
            Palette.RandomColor(),
            SampleProjectContent.BoardStatuses,
            cancellationToken);

        var boardStatusIds = await context.ActiveStatuses()
            .Where(x => x.EpicId == epicId)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .ToArrayAsyncEF(cancellationToken);

        var backlogStatusId = await context.ActiveStatuses()
            .Where(x => x.Epic!.SpaceId == spaceId && x.Epic.IsDefault)
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Id)
            .FirstAsyncEF(cancellationToken);

        var issueIds = new long[SampleProjectContent.Issues.Length];
        for (var index = 0; index < SampleProjectContent.Issues.Length; index++)
        {
            var sample = SampleProjectContent.Issues[index];
            var statusId = sample.StatusIndex is { } statusIndex ? boardStatusIds[statusIndex] : backlogStatusId;

            var request = new IssueCreateRequest(statusId, dateTimeProvider.UtcNow, sample.Title, isTitleSetExplicitly: true)
                .SetContent(sample.Content)
                .MarkAsSample();

            issueIds[index] = await coreIssuesService.Create(userId, request, cancellationToken);

            if (sample.Comment is not null)
                await coreIssuesService.AddComment(issueIds[index], userId, sample.Comment, [], cancellationToken);
        }

        for (var index = 0; index < SampleProjectContent.Issues.Length; index++)
        {
            if (SampleProjectContent.Issues[index].BlockedByIndex is { } blockerIndex)
            {
                await coreIssueLinksService.AddLink(
                    issueIds[blockerIndex],
                    IssueLinkRelation.Blocks,
                    issueIds[index],
                    userId,
                    cancellationToken);
            }
        }
    }
}

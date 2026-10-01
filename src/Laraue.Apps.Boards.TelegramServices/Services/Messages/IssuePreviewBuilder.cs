using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.Services.Members;
using Laraue.Apps.Boards.TelegramServices.Services.Search;
using LinqToDB.EntityFrameworkCore;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface IIssuePreviewBuilder
{
    /// <summary>
    /// Builds the same key/org/title + link shown for an inline search result, so a
    /// /save or /info reply looks like the same "card" wherever it's shown from.
    /// </summary>
    Task<IssuePreview> Build(long issueId, CancellationToken cancellationToken);
}

public class IssuePreviewBuilder(
    DatabaseContext context,
    IIssueUrlBuilder issueUrlBuilder,
    IMemberProfileReader memberProfileReader)
    : IIssuePreviewBuilder
{
    public async Task<IssuePreview> Build(long issueId, CancellationToken cancellationToken)
    {
        var issueData = await context.ActiveIssues()
            .Where(x => x.Id == issueId)
            .Select(x => new
            {
                Key = new IssueKey(x.IssueNumber!.Space!.Key, x.IssueNumber.Number),
                x.Title,
                OrganizationName = x.IssueNumber.Space.Organization!.Name,
                OrganizationSlug = x.IssueNumber.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.IssueNumber.Space.Organization!.SlugPostfix,
                ChatTitle = x.TelegramMessage != null ? x.TelegramMessage.LinkedTelegramChat!.Title : null,
                x.IssueNumber.Space.OrganizationId,
                SenderId = x.TelegramMessage != null ? x.TelegramMessage.SenderId : null,
                SentAt = x.TelegramMessage != null ? x.TelegramMessage.SentAt : null,
            })
            .FirstAsyncEF(cancellationToken);

        var url = issueUrlBuilder.Build(issueData.OrganizationSlug, issueData.OrganizationSlugPostfix, issueData.Key);

        var sender = issueData.SenderId is { } senderId ? new UserDetails { UserId = senderId } : null;
        if (sender is not null)
            await memberProfileReader.EnrichUsers(
                issueData.OrganizationId,
                [sender],
                cancellationToken);

        // The card shows the issue's title only, never its content.
        var footer = IssuePreviewFormatter.BuildSourceFooter(issueData.ChatTitle, sender?.DisplayName, issueData.SentAt);

        var text = IssuePreviewFormatter.BuildHeader(issueData.Key, issueData.OrganizationName) + "\n" +
            SearchTextFormatter.EscapeMarkdownV2(SearchTextFormatter.NormalizeWhitespace(issueData.Title));
        if (footer is not null)
            text += "\n" + footer;

        return new IssuePreview { Text = text, Url = url };
    }
}

/// <summary>
/// The same key/org/title "card" text + link shown for an inline search result,
/// /save, and /info replies alike.
/// </summary>
public class IssuePreview
{
    /// <summary>MarkdownV2 "📋 KEY · Org\n{title}" text.</summary>
    public required string Text { get; init; }

    public required string Url { get; init; }
}

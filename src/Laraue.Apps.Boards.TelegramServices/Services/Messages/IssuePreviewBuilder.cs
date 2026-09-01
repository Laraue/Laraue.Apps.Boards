using Laraue.Apps.Boards.DataAccess;
using Laraue.Apps.Boards.Services;
using Laraue.Apps.Boards.TelegramServices.Services.Search;
using LinqToDB.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.TelegramServices.Services.Messages;

public interface IIssuePreviewBuilder
{
    /// <summary>
    /// Builds the same key/org/content-preview + link shown for an inline search result, so a
    /// /save or /info reply looks like the same "card" wherever it's shown from.
    /// </summary>
    Task<IssuePreview> Build(long issueId, CancellationToken cancellationToken);
}

public class IssuePreviewBuilder(
    DatabaseContext context,
    IIssueUrlBuilder issueUrlBuilder,
    ILogger<IssuePreviewBuilder> logger)
    : IIssuePreviewBuilder
{
    public async Task<IssuePreview> Build(long issueId, CancellationToken cancellationToken)
    {
        var issueData = await context.Issues
            .Where(x => x.Id == issueId)
            .Select(x => new
            {
                Key = new IssueKey(x.IssueNumber!.Space!.Key, x.IssueNumber.Number),
                OrganizationName = x.IssueNumber.Space.Organization!.Name,
                OrganizationSlug = x.IssueNumber.Space.Organization!.Slug,
                OrganizationSlugPostfix = x.IssueNumber.Space.Organization!.SlugPostfix,
                x.Content,
                ChatTitle = x.TelegramMessage != null ? x.TelegramMessage.LinkedTelegramChat!.Title : null,
                SenderName = x.TelegramMessage != null && x.TelegramMessage.Sender != null
                    ? (x.TelegramMessage.Sender.TelegramUserName ?? x.TelegramMessage.Sender.TelegramFirstName)
                    : null,
                SentAt = x.TelegramMessage != null ? x.TelegramMessage.SentAt : null,
            })
            .FirstAsyncEF(cancellationToken);

        var url = issueUrlBuilder.Build(issueData.OrganizationSlug, issueData.OrganizationSlugPostfix, issueData.Key);

        string text;
        try
        {
            var fragment = ContentFragment.Extract(
                issueData.Content ?? string.Empty,
                searchText: string.Empty,
                IssuePreviewFormatter.FragmentContextChars);

            var footer = IssuePreviewFormatter.BuildSourceFooter(issueData.ChatTitle, issueData.SenderName, issueData.SentAt);

            text = IssuePreviewFormatter.BuildHeader(issueData.Key, issueData.OrganizationName) + "\n" + fragment.ToMarkdownV2();
            if (footer is not null)
                text += "\n" + footer;
        }
        catch (Exception ex)
        {
            // A bug in formatting this one issue's content shouldn't fail the whole /save or
            // /info reply - log it and hand back a safe placeholder instead.
            logger.LogError(ex, "Issue {IssueKey}: failed to build preview content", issueData.Key);
            text = IssuePreviewFormatter.BuildContentGenerationErrorText(issueData.Key, issueData.OrganizationName);
        }

        return new IssuePreview { Text = text, Url = url };
    }
}

/// <summary>
/// The same key/org/content-preview "card" text + link shown for an inline search result,
/// /save, and /info replies alike.
/// </summary>
public class IssuePreview
{
    /// <summary>MarkdownV2 "📋 KEY · Org\n{content preview}" text.</summary>
    public required string Text { get; init; }

    public required string Url { get; init; }
}

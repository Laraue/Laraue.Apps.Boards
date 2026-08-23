namespace Laraue.Apps.Boards.Services.Ai;

/// <summary>
/// Rewrites chaotic user notes into a structured task description.
/// </summary>
public interface IAiContentSummarizer
{
    /// <summary>
    /// Returns a markdown document: a title line, then a "---" separator line,
    /// then the structured task content.
    /// </summary>
    Task<string> SummarizeAsync(string notes, CancellationToken cancellationToken);
}

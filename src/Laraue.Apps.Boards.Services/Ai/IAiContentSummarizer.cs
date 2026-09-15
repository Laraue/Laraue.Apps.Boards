namespace Laraue.Apps.Boards.Services.Ai;

/// <summary>
/// Rewrites chaotic user notes into a structured task description.
/// </summary>
public interface IAiContentSummarizer
{
    /// <summary>
    /// The provider's own ceiling on generated tokens per call - needed by a caller that reserves
    /// Billing tokens before calling <see cref="SummarizeAsync"/>, since the reservation has to
    /// cover the worst case before the actual output size is known.
    /// </summary>
    int MaxOutputTokensCount { get; }

    /// <summary>
    /// Runs <paramref name="notes"/> through the AI provider and returns the beautified content
    /// (a markdown document: a title line, then a "---" separator line, then the structured task
    /// content) alongside the actual input/output token counts the provider billed for.
    /// </summary>
    Task<AiSummarizationResult> SummarizeAsync(string notes, CancellationToken cancellationToken);
}

/// <summary>
/// <paramref name="InputTokensCount"/>/<paramref name="OutputTokensCount"/> are the provider's own
/// reported usage for the call, not an estimate - needed to commit an accurate amount back to
/// Billing after reserving a conservative estimate up front.
/// </summary>
public sealed record AiSummarizationResult(string Content, int InputTokensCount, int OutputTokensCount);

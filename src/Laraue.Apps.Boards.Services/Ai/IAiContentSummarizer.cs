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
    /// Estimated total input tokens a call to <see cref="SummarizeAsync"/> with
    /// <paramref name="content"/> will cost - <em>not</em> just an estimate of
    /// <paramref name="content"/> itself (and it depends on <paramref name="generateTitle"/>, which
    /// picks the system prompt), since this implementation also sends its own fixed
    /// overhead on every call (a system prompt, chat-formatting overhead, etc.) that a caller has
    /// no visibility into. A caller reserving Billing tokens before calling
    /// <see cref="SummarizeAsync"/> should use this rather than estimating the content on its own,
    /// or the reservation silently undercounts by however large that fixed overhead is. Discovered
    /// from a real run where a content-only estimate was 9 tokens but the provider's actual
    /// reported usage was 106 - the ~97-token gap was entirely the missing overhead.
    /// </summary>
    int EstimateInputTokenCount(string content, bool generateTitle);

    /// <summary>
    /// Runs <paramref name="notes"/> through the AI provider and returns the beautified content and,
    /// when <paramref name="generateTitle"/> is set and the provider returned one, a generated title, alongside the actual
    /// input/output token counts the provider billed for.
    /// </summary>
    Task<AiSummarizationResult> SummarizeAsync(string notes, bool generateTitle, CancellationToken cancellationToken);

    /// <summary>
    /// The ceiling on generated tokens of a <see cref="GenerateTitleAsync"/> call - a title is a line, so
    /// far below <see cref="MaxOutputTokensCount"/>, which keeps the Billing reservation small.
    /// </summary>
    int MaxTitleOutputTokensCount { get; }

    /// <summary>
    /// Same as <see cref="EstimateInputTokenCount"/>, for a <see cref="GenerateTitleAsync"/> call.
    /// </summary>
    int EstimateTitleInputTokenCount(string content);

    /// <summary>
    /// Asks the AI provider for a title of <paramref name="notes"/> only - the text is not rewritten, so
    /// the call is short and cheap. The title is null when the provider gave none.
    /// </summary>
    Task<AiTitleResult> GenerateTitleAsync(string notes, CancellationToken cancellationToken);
}

/// <summary>
/// A generated title with the provider's own reported usage, like <see cref="AiSummarizationResult"/>.
/// </summary>
public sealed record AiTitleResult(string? Title, int InputTokensCount, int OutputTokensCount);

/// <summary>
/// <paramref name="InputTokensCount"/>/<paramref name="OutputTokensCount"/> are the provider's own
/// reported usage for the call, not an estimate - needed to commit an accurate amount back to
/// Billing after reserving a conservative estimate up front.
/// </summary>
public sealed record AiSummarizationResult(string? Title, string Content, int InputTokensCount, int OutputTokensCount);

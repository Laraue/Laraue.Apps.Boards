using Microsoft.Extensions.Logging;

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// A pre-call estimate of input token count for reserving Billing tokens before the AI provider is
/// actually called. Unlike output tokens, this is never corrected afterwards - Billing's
/// <c>TokenService.CommitTokensSpentAsync</c> bills off the reserved input count, not anything the
/// provider actually reports - so this estimate <em>is</em> the final input-token charge, not a
/// placeholder. An under-estimate (e.g. for a script this heuristic weights too cheaply) is an
/// accepted, absorbed cost rather than something corrected later - see
/// <see cref="LogIfEstimateDiverges"/> for how that's tracked instead of silently ignored.
/// </summary>
public interface ITokenEstimate
{
    /// <summary>
    /// Splits on ASCII vs. non-ASCII per character rather than trying to detect specific scripts -
    /// cheap, and handles genuinely mixed-language content (e.g. a note switching between English
    /// and Russian) correctly by construction, since each character is weighted on its own.
    /// </summary>
    int EstimateInputTokenCount(string content);

    /// <summary>
    /// Compares the pre-call estimate against DeepSeek's own reported <c>prompt_tokens</c>
    /// (<c>AiSummarizationResult.InputTokensCount</c>) once it's known, logging a warning when they
    /// diverge by more than 10% - the only signal available for whether the ratios need adjusting,
    /// since the estimate itself is never corrected.
    /// </summary>
    void LogIfEstimateDiverges(int estimatedInputTokensCount, int actualInputTokensCount);
}

public class TokenEstimate(ILogger<TokenEstimate> logger) : ITokenEstimate
{
    /// <summary>DeepSeek's own published ratio for English text (~0.3 tokens/char).</summary>
    private const double AsciiTokensPerChar = 0.3;

    /// <summary>
    /// DeepSeek's own published ratio for Chinese text (~0.6 tokens/char) - used as a conservative
    /// stand-in for any non-Latin script (Cyrillic included), since BPE vocabularies trained mostly
    /// on Latin/CJK text tend to fragment other scripts just as densely.
    /// </summary>
    private const double NonAsciiTokensPerChar = 0.6;

    /// <summary>
    /// A run diverging from DeepSeek's real reported count by more than this fraction gets logged,
    /// so the ratios above can be tuned from real data instead of guessed at indefinitely.
    /// </summary>
    private const double DivergenceWarningThreshold = 0.1;

    public int EstimateInputTokenCount(string content)
    {
        var nonAsciiCount = content.Count(c => c > 127);
        var asciiCount = content.Length - nonAsciiCount;

        var estimate = (asciiCount * AsciiTokensPerChar) + (nonAsciiCount * NonAsciiTokensPerChar);

        return Math.Max(1, (int)Math.Ceiling(estimate));
    }

    public void LogIfEstimateDiverges(int estimatedInputTokensCount, int actualInputTokensCount)
    {
        if (actualInputTokensCount <= 0)
        {
            return;
        }

        var divergence = Math.Abs(actualInputTokensCount - estimatedInputTokensCount) / (double)actualInputTokensCount;
        if (divergence > DivergenceWarningThreshold)
        {
            logger.LogWarning(
                "Input token estimate diverged from actual by {DivergencePercent:P0}: estimated {Estimated}, actual {Actual}",
                divergence,
                estimatedInputTokensCount,
                actualInputTokensCount);
        }
    }
}

namespace Laraue.Apps.Boards.Services.Billing;

/// <summary>
/// A rough, deliberately generous pre-call estimate of input token count for reserving Billing
/// tokens before the AI provider is actually called - refined at commit time with the provider's
/// own reported usage (<c>AiSummarizationResult.InputTokensCount</c>), so this only needs to be in
/// the right ballpark, not exact. Shared by both AI-summarize call sites (web API, Telegram) rather
/// than each host re-deriving its own estimate.
/// </summary>
public static class TokenEstimate
{
    /// <summary>~4 characters per token is a common rough approximation for English text.</summary>
    public static int EstimateInputTokenCount(string content) => Math.Max(1, content.Length / 4);
}

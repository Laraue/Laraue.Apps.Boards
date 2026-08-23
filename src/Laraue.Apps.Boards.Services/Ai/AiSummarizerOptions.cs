namespace Laraue.Apps.Boards.Services.Ai;

/// <summary>
/// Settings for an OpenAI-compatible chat-completions API used for content summarization
/// (a local Ollama instance by default - see README.md - overridden with a real provider on prod).
/// </summary>
public class AiSummarizerOptions
{
    /// <summary>
    /// API key sent as a bearer token.
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// API base address.
    /// </summary>
    public required string BaseUrl { get; set; }

    /// <summary>
    /// Model to use for chat completions.
    /// </summary>
    public required string Model { get; set; }

    /// <summary>
    /// Whether to let the model "think" (extended chain-of-thought) before answering.
    /// This task doesn't need it, and it substantially slows down responses on models
    /// that support toggling it - defaults to disabled.
    /// </summary>
    public bool Thinking { get; set; }
}

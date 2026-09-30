using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Laraue.Apps.Boards.Services.Billing;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services.Ai;

/// <summary>
/// Calls an OpenAI-compatible chat-completions API (DeepSeek, Ollama, ...) to summarize notes.
/// </summary>
public class OpenAiCompatibleContentSummarizer(
    HttpClient httpClient,
    IOptions<AiSummarizerOptions> options,
    ITokenEstimate tokenEstimate)
    : IAiContentSummarizer
{
    private const string BasePrompt =
        """
        Beautify these task notes: fix grammar, spelling, formatting, structure; remove
        duplicate statements. Wording only - never change meaning, never add anything not
        already present (no new facts, steps, examples, explanations, or elaboration on what
        was only briefly mentioned) - leave unclear or incomplete parts as-is rather than
        filling them in. Keep the same length and level of detail as the input; don't turn a
        short note into sections, labels, or a list it didn't have (e.g. no invented
        "Issue:"/"Task:" headers or restating one point as several).
        """;

    private const string TitleFormatPrompt =
        """
        Reply with a JSON object of two string fields: "title" - a short one-line title (keep
        an existing title as-is, else derive one from the notes only), and "content" - the
        beautified markdown content without the title. No code block, no extra commentary.
        Example: {"title": "Fix login retry", "content": "- Login fails on retry\n- Add logging"}
        """;

    private const string ContentOnlyFormatPrompt =
        """
        Reply with a JSON object of one string field: "content" - the beautified markdown
        content. No code block, no extra commentary.
        Example: {"content": "- Login fails on retry\n- Add logging"}
        """;

    private const string SystemPromptWithTitle = BasePrompt + "\n" + TitleFormatPrompt;

    private const string SystemPromptWithoutTitle = BasePrompt + "\n" + ContentOnlyFormatPrompt;

    private static string GetSystemPrompt(bool generateTitle) =>
        generateTitle ? SystemPromptWithTitle : SystemPromptWithoutTitle;

    private const int DefaultMaxTokens = 2048;

    public int MaxOutputTokensCount => DefaultMaxTokens;

    // The system prompts are compile-time constants sent unchanged on every call, so their
    // estimates never change either - cheap enough (a ~400-char string) that recomputing them per
    // call isn't worth caching.
    public int EstimateInputTokenCount(string content, bool generateTitle) =>
        tokenEstimate.EstimateInputTokenCount(GetSystemPrompt(generateTitle))
        + tokenEstimate.EstimateInputTokenCount(content);

    public async Task<AiSummarizationResult> SummarizeAsync(
        string notes,
        bool generateTitle,
        CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest
        {
            Model = options.Value.Model,
            Messages =
            [
                new ChatMessage { Role = "system", Content = GetSystemPrompt(generateTitle) },
                new ChatMessage { Role = "user", Content = notes },
            ],
            Thinking = new ChatCompletionThinking
            {
                Type = options.Value.Thinking ? "enabled" : "disabled",
            },
            ResponseFormat = new ChatCompletionResponseFormat { Type = "json_object" },
            MaxTokens = DefaultMaxTokens,
            Stream = false,
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync("chat/completions", request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException ex)
        {
            throw new AiContentSummarizationException("AI summarization API request failed.", ex);
        }

        var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken)
            ?? throw new AiContentSummarizationException("AI summarization API returned an empty response.");

        var content = completion.Choices.FirstOrDefault()?.Message.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiContentSummarizationException("AI summarization API returned no completion content.");
        }

        // Billing needs the provider's own token accounting to commit an accurate amount rather
        // than a guess - fail loud instead of committing made-up numbers if it's ever missing.
        if (completion.Usage is not { } usage)
        {
            throw new AiContentSummarizationException("AI summarization API returned no usage data.");
        }

        var (title, body) = ParseSummary(content, generateTitle);

        return new AiSummarizationResult(title, body, usage.PromptTokens, usage.CompletionTokens);
    }

    /// <summary>
    /// Reads the requested {"title", "content"} (or content-only) JSON. The tokens are already spent,
    /// so a reply that isn't that JSON or lacks the content is kept as plain content with no title,
    /// and a missing title just leaves the issue to derive it from the first line.
    /// </summary>
    private static (string? Title, string Content) ParseSummary(string completion, bool generateTitle)
    {
        SummaryPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SummaryPayload>(completion);
        }
        catch (JsonException)
        {
            payload = null;
        }

        var content = payload?.Content?.Trim();
        if (string.IsNullOrEmpty(content))
            return (null, completion.Trim());

        var title = generateTitle ? IssueTitle.FromContent(payload?.Title) : string.Empty;

        return (title.Length == 0 ? null : title, content);
    }

    private record SummaryPayload
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private record ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required ChatMessage[] Messages { get; init; }

        [JsonPropertyName("thinking")]
        public required ChatCompletionThinking Thinking { get; init; }

        [JsonPropertyName("response_format")]
        public required ChatCompletionResponseFormat ResponseFormat { get; init; }

        [JsonPropertyName("max_tokens")]
        public required int MaxTokens { get; init; }

        [JsonPropertyName("stream")]
        public required bool Stream { get; init; }
    }

    private record ChatCompletionThinking
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }
    }

    private record ChatCompletionResponseFormat
    {
        [JsonPropertyName("type")]
        public required string Type { get; init; }
    }

    private record ChatMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private record ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public required ChatCompletionChoice[] Choices { get; init; }

        [JsonPropertyName("usage")]
        public ChatCompletionUsage? Usage { get; init; }
    }

    private record ChatCompletionChoice
    {
        [JsonPropertyName("message")]
        public required ChatMessage Message { get; init; }
    }

    private record ChatCompletionUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public required int PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public required int CompletionTokens { get; init; }
    }
}

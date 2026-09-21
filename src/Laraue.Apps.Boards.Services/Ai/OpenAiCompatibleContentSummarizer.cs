using System.Net.Http.Json;
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
    private const string SystemPrompt =
        """
        Beautify these task notes: fix grammar, spelling, formatting, structure; remove
        duplicate statements. Wording only - never change meaning, never add anything not
        already present (no new facts, steps, examples, explanations, or elaboration on what
        was only briefly mentioned) - leave unclear or incomplete parts as-is rather than
        filling them in. Keep the same length and level of detail as the input; don't turn a
        short note into sections, labels, or a list it didn't have (e.g. no invented
        "Issue:"/"Task:" headers or restating one point as several).
        Output markdown only: title line, then a line with only "---", then the beautified
        content. Keep an existing title as-is; else derive a short one from the notes only.
        No code block, no extra commentary.
        """;

    private const int DefaultMaxTokens = 2048;

    public int MaxOutputTokensCount => DefaultMaxTokens;

    // SystemPrompt is a compile-time constant sent unchanged on every call, so its estimate never
    // changes either - cheap enough (a ~400-char string) that recomputing it per access isn't
    // worth caching.
    private int SystemPromptTokensCount => tokenEstimate.EstimateInputTokenCount(SystemPrompt);

    public int EstimateInputTokenCount(string content) =>
        SystemPromptTokensCount + tokenEstimate.EstimateInputTokenCount(content);

    public async Task<AiSummarizationResult> SummarizeAsync(string notes, CancellationToken cancellationToken)
    {
        var request = new ChatCompletionRequest
        {
            Model = options.Value.Model,
            Messages =
            [
                new ChatMessage { Role = "system", Content = SystemPrompt },
                new ChatMessage { Role = "user", Content = notes },
            ],
            Thinking = new ChatCompletionThinking
            {
                Type = options.Value.Thinking ? "enabled" : "disabled",
            },
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

        return new AiSummarizationResult(content.Trim(), usage.PromptTokens, usage.CompletionTokens);
    }

    private record ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required ChatMessage[] Messages { get; init; }

        [JsonPropertyName("thinking")]
        public required ChatCompletionThinking Thinking { get; init; }

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

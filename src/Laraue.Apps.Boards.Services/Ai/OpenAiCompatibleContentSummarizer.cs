using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.Services.Ai;

/// <summary>
/// Calls an OpenAI-compatible chat-completions API (DeepSeek, Ollama, ...) to summarize notes.
/// </summary>
public class OpenAiCompatibleContentSummarizer(HttpClient httpClient, IOptions<AiSummarizerOptions> options)
    : IAiContentSummarizer
{
    private const string SystemPrompt =
        """
        Beautify these task notes: fix grammar, spelling, formatting and structure, remove
        duplicate statements. Never add facts or content not already present; never change
        meaning - wording only.
        Output markdown only, shape: title line, then a line with only "---", then the
        beautified content. Keep an existing title as-is (beautified only); else derive a
        short title from the notes. No code block, no extra commentary.
        """;

    private const int DefaultMaxTokens = 2048;

    public async Task<string> SummarizeAsync(string notes, CancellationToken cancellationToken)
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

        return content.Trim();
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
    }

    private record ChatCompletionChoice
    {
        [JsonPropertyName("message")]
        public required ChatMessage Message { get; init; }
    }
}

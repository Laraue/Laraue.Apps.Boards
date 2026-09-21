using System.Net;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Ai;
using Laraue.Apps.Boards.Services.Billing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Laraue.Apps.Boards.IntegrationTests;

public class OpenAiCompatibleContentSummarizerTests
{
    private static OpenAiCompatibleContentSummarizer CreateSummarizer(
        FakeHttpMessageHandler handler,
        bool thinking = false)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://ai.example.com/"),
        };

        var options = Options.Create(new AiSummarizerOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://ai.example.com/",
            Model = "test-model",
            Thinking = thinking,
        });

        return new OpenAiCompatibleContentSummarizer(httpClient, options, new TokenEstimate(NullLogger<TokenEstimate>.Instance));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public void EstimateInputTokenCount_ShouldIncludeSystemPromptOverhead_Always()
    {
        // Regression guard for a real production gap: this used to only estimate the caller's own
        // content, so a short note (e.g. ~9 estimated tokens) silently under-reserved by the
        // system prompt's own cost (observed as high as ~97 tokens in practice - DeepSeek's
        // reported prompt_tokens includes the whole request, not just the caller's content).
        var summarizer = CreateSummarizer(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("not used")));

        var estimate = summarizer.EstimateInputTokenCount(string.Empty);

        Assert.True(estimate > 20);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldReturnTrimmedCompletionContentAndUsage_WhenApiRespondsSuccessfully()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {
                "choices":[{"message":{"role":"assistant","content":"  Fix login bug\n---\nBeautified content  "}}],
                "usage":{"prompt_tokens":42,"completion_tokens":17}
            }
            """)));

        var summarizer = CreateSummarizer(handler);

        var result = await summarizer.SummarizeAsync("fix login bug pls", CancellationToken.None);

        Assert.Equal("Fix login bug\n---\nBeautified content", result.Content);
        Assert.Equal(42, result.InputTokensCount);
        Assert.Equal(17, result.OutputTokensCount);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldSendThinkingDisabled_WhenThinkingOptionIsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")));

        var summarizer = CreateSummarizer(handler, thinking: false);

        await summarizer.SummarizeAsync("notes", CancellationToken.None);

        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.LastRequestBody);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldSendThinkingEnabled_WhenThinkingOptionIsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")));

        var summarizer = CreateSummarizer(handler, thinking: true);

        await summarizer.SummarizeAsync("notes", CancellationToken.None);

        Assert.Contains("\"thinking\":{\"type\":\"enabled\"}", handler.LastRequestBody);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldThrowAiContentSummarizationException_WhenApiReturnsNonSuccessStatusCode()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.InternalServerError,
            """{"error":"boom"}""")));

        var summarizer = CreateSummarizer(handler);

        var ex = await Assert.ThrowsAsync<AiContentSummarizationException>(
            () => summarizer.SummarizeAsync("notes", CancellationToken.None));

        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldThrowAiContentSummarizationException_WhenApiReturnsNoChoices()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")));

        var summarizer = CreateSummarizer(handler);

        await Assert.ThrowsAsync<AiContentSummarizationException>(
            () => summarizer.SummarizeAsync("notes", CancellationToken.None));
    }

    [Fact]
    public async Task SummarizeAsync_ShouldThrowAiContentSummarizationException_WhenApiReturnsNoUsage()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}]}""")));

        var summarizer = CreateSummarizer(handler);

        await Assert.ThrowsAsync<AiContentSummarizationException>(
            () => summarizer.SummarizeAsync("notes", CancellationToken.None));
    }
}

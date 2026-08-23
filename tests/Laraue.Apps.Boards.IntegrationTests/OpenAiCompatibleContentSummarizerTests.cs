using System.Net;
using Laraue.Apps.Boards.IntegrationTests.Infrastructure;
using Laraue.Apps.Boards.Services.Ai;
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

        return new OpenAiCompatibleContentSummarizer(httpClient, options);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task SummarizeAsync_ShouldReturnTrimmedCompletionContent_WhenApiRespondsSuccessfully()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"  Fix login bug\n---\nBeautified content  "}}]}""")));

        var summarizer = CreateSummarizer(handler);

        var result = await summarizer.SummarizeAsync("fix login bug pls", CancellationToken.None);

        Assert.Equal("Fix login bug\n---\nBeautified content", result);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldSendThinkingDisabled_WhenThinkingOptionIsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}]}""")));

        var summarizer = CreateSummarizer(handler, thinking: false);

        await summarizer.SummarizeAsync("notes", CancellationToken.None);

        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.LastRequestBody);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldSendThinkingEnabled_WhenThinkingOptionIsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}]}""")));

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
            """{"choices":[]}""")));

        var summarizer = CreateSummarizer(handler);

        await Assert.ThrowsAsync<AiContentSummarizationException>(
            () => summarizer.SummarizeAsync("notes", CancellationToken.None));
    }
}

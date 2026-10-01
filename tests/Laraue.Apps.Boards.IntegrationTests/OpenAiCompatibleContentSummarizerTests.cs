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
        bool thinking = false,
        bool useJsonSchema = false)
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
            UseJsonSchema = useJsonSchema,
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

        var estimate = summarizer.EstimateInputTokenCount(string.Empty, generateTitle: true);

        Assert.True(estimate > 20);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldReturnTitleContentAndUsage_WhenApiRepliesWithJson()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {
                "choices":[{"message":{"role":"assistant","content":"{\"title\":\"# Fix login bug\",\"content\":\"  Beautified\\n---\\ncontent  \"}"}}],
                "usage":{"prompt_tokens":42,"completion_tokens":17}
            }
            """)));

        var summarizer = CreateSummarizer(handler);

        var result = await summarizer.SummarizeAsync("fix login bug pls", generateTitle: true, CancellationToken.None);

        Assert.Equal("Fix login bug", result.Title);
        Assert.Equal("Beautified\n---\ncontent", result.Content);
        Assert.Equal(42, result.InputTokensCount);
        Assert.Equal(17, result.OutputTokensCount);
    }

    [Theory]
    [InlineData("Plain text reply", null, "Plain text reply")]
    [InlineData("{\"title\":\"\",\"content\":\"Body\"}", null, "Body")]
    [InlineData("{\"title\":\"Feedback Collection Service\"}", "Feedback Collection Service", "my own notes")]
    public async Task SummarizeAsync_ShouldNeverReplaceTheTextWithBrokenJson_WhenTheModelAnswersWithLessThanAsked(
        string reply,
        string? expectedTitle,
        string expectedContent)
    {
        var summarizer = CreateSummarizer(new FakeHttpMessageHandler(_ => Task.FromResult(ReplyOf(reply))));

        var result = await summarizer.SummarizeAsync("my own notes", generateTitle: true, CancellationToken.None);

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(expectedContent, result.Content);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":\"\",\"content\":\"\"}")]
    [InlineData("{\"title\": \"Cut off\", \"content\": \"Body that never ends")]
    public async Task SummarizeAsync_ShouldThrow_WhenTheReplyHasNothingUsable(string reply)
    {
        var summarizer = CreateSummarizer(new FakeHttpMessageHandler(_ => Task.FromResult(ReplyOf(reply))));

        await Assert.ThrowsAsync<AiContentSummarizationException>(
            () => summarizer.SummarizeAsync("my own notes", generateTitle: true, CancellationToken.None));
    }

    private static HttpResponseMessage ReplyOf(string reply) => JsonResponse(
        HttpStatusCode.OK,
        System.Text.Json.JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { role = "assistant", content = reply } } },
            usage = new { prompt_tokens = 1, completion_tokens = 1 },
        }));

    [Theory]
    [InlineData(true, true, "\"required\":[\"title\",\"content\"]")]
    [InlineData(true, false, "\"required\":[\"content\"]")]
    [InlineData(false, true, "\"response_format\":{\"type\":\"json_object\"}")]
    public async Task SummarizeAsync_ShouldSendTheSchemaOfTheReply_WhenJsonSchemaIsEnabled(
        bool useJsonSchema,
        bool generateTitle,
        string expectedInRequest)
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(ReplyOf("{\"title\":\"T\",\"content\":\"C\"}")));
        var summarizer = CreateSummarizer(handler, useJsonSchema: useJsonSchema);

        await summarizer.SummarizeAsync("notes", generateTitle, CancellationToken.None);

        Assert.Contains(expectedInRequest, handler.LastRequestBody);
        Assert.Equal(useJsonSchema, handler.LastRequestBody!.Contains("\"json_schema\""));
    }

    [Fact]
    public async Task GenerateTitleAsync_ShouldRequireOnlyTheTitle_WhenJsonSchemaIsEnabled()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(ReplyOf("{\"title\":\"T\"}")));
        var summarizer = CreateSummarizer(handler, useJsonSchema: true);

        await summarizer.GenerateTitleAsync("notes", CancellationToken.None);

        Assert.Contains("\"required\":[\"title\"]", handler.LastRequestBody);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldAskForContentOnlyAndReturnNoTitle_WhenTitleIsNotRequested()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {
                "choices":[{"message":{"role":"assistant","content":"{\"title\":\"Ignored\",\"content\":\"Body\"}"}}],
                "usage":{"prompt_tokens":1,"completion_tokens":1}
            }
            """)));

        var summarizer = CreateSummarizer(handler);

        var result = await summarizer.SummarizeAsync("notes", generateTitle: false, CancellationToken.None);

        Assert.Null(result.Title);
        Assert.Equal("Body", result.Content);
        Assert.DoesNotContain("\\\"title\\\"", handler.LastRequestBody);
    }

    [Fact]
    public void EstimateInputTokenCount_ShouldBeSmaller_WhenTitleIsNotRequested()
    {
        var summarizer = CreateSummarizer(new FakeHttpMessageHandler(_ => throw new InvalidOperationException()));

        Assert.True(
            summarizer.EstimateInputTokenCount("notes", generateTitle: false)
            < summarizer.EstimateInputTokenCount("notes", generateTitle: true));
    }

    [Theory]
    [InlineData("{\\\"title\\\":\\\"# Fix login retry\\\"}", "Fix login retry")]
    [InlineData("Fix login retry. And more", "Fix login retry")]
    [InlineData("{\\\"title\\\":\\\"\\\"}", null)]
    public async Task GenerateTitleAsync_ShouldReturnOnlyTheTitleAndUsage_WhenApiReplies(string reply, string? expectedTitle)
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"" + reply + "\"}}],"
            + "\"usage\":{\"prompt_tokens\":30,\"completion_tokens\":7}}")));

        var summarizer = CreateSummarizer(handler);

        var result = await summarizer.GenerateTitleAsync("fix login bug pls", CancellationToken.None);

        Assert.Equal(expectedTitle, result.Title);
        Assert.Equal(30, result.InputTokensCount);
        Assert.Equal(7, result.OutputTokensCount);
        // A title is a line: the call is capped far below a rewrite of the whole text.
        Assert.Contains("\"max_tokens\":" + summarizer.MaxTitleOutputTokensCount, handler.LastRequestBody);
        Assert.True(summarizer.MaxTitleOutputTokensCount < summarizer.MaxOutputTokensCount);
    }

    [Fact]
    public void EstimateTitleInputTokenCount_ShouldBeSmallerThanARewrite_Always()
    {
        var summarizer = CreateSummarizer(new FakeHttpMessageHandler(_ => throw new InvalidOperationException()));

        Assert.True(
            summarizer.EstimateTitleInputTokenCount("notes")
            < summarizer.EstimateInputTokenCount("notes", generateTitle: true));
    }

    [Fact]
    public async Task SummarizeAsync_ShouldSendThinkingDisabled_WhenThinkingOptionIsFalse()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")));

        var summarizer = CreateSummarizer(handler, thinking: false);

        await summarizer.SummarizeAsync("notes", generateTitle: true, CancellationToken.None);

        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.LastRequestBody);
    }

    [Fact]
    public async Task SummarizeAsync_ShouldSendThinkingEnabled_WhenThinkingOptionIsTrue()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""")));

        var summarizer = CreateSummarizer(handler, thinking: true);

        await summarizer.SummarizeAsync("notes", generateTitle: true, CancellationToken.None);

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
            () => summarizer.SummarizeAsync("notes", generateTitle: true, CancellationToken.None));

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
            () => summarizer.SummarizeAsync("notes", generateTitle: true, CancellationToken.None));
    }

    [Fact]
    public async Task SummarizeAsync_ShouldThrowAiContentSummarizationException_WhenApiReturnsNoUsage()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Title\n---\nContent"}}]}""")));

        var summarizer = CreateSummarizer(handler);

        await Assert.ThrowsAsync<AiContentSummarizationException>(
            () => summarizer.SummarizeAsync("notes", generateTitle: true, CancellationToken.None));
    }
}

using Laraue.Apps.Boards.Services.Billing;
using Microsoft.Extensions.Logging;
using Moq;

namespace Laraue.Apps.Boards.IntegrationTests;

public class TokenEstimateTests
{
    private readonly Mock<ILogger<TokenEstimate>> _logger = new();
    private readonly TokenEstimate _tokenEstimate;

    public TokenEstimateTests()
    {
        _tokenEstimate = new TokenEstimate(_logger.Object);
    }

    [Fact]
    public void EstimateInputTokenCount_ShouldUseAsciiRatio_WhenContentIsEnglish()
    {
        var content = new string('a', 100);

        var result = _tokenEstimate.EstimateInputTokenCount(content);

        Assert.Equal(30, result);
    }

    [Fact]
    public void EstimateInputTokenCount_ShouldUseNonAsciiRatio_WhenContentIsCyrillic()
    {
        var content = new string('а', 100);

        var result = _tokenEstimate.EstimateInputTokenCount(content);

        Assert.Equal(60, result);
    }

    [Fact]
    public void EstimateInputTokenCount_ShouldBlendRatios_WhenContentIsMixed()
    {
        var content = new string('a', 100) + new string('а', 100);

        var result = _tokenEstimate.EstimateInputTokenCount(content);

        Assert.Equal(90, result);
    }

    [Fact]
    public void EstimateInputTokenCount_ShouldReturnAtLeastOne_WhenContentIsEmpty()
    {
        var result = _tokenEstimate.EstimateInputTokenCount(string.Empty);

        Assert.Equal(1, result);
    }

    [Fact]
    public void LogIfEstimateDiverges_ShouldLogWarning_WhenDivergenceExceedsThreshold()
    {
        _tokenEstimate.LogIfEstimateDiverges(estimatedInputTokensCount: 100, actualInputTokensCount: 200);

        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void LogIfEstimateDiverges_ShouldNotLog_WhenDivergenceWithinThreshold()
    {
        _tokenEstimate.LogIfEstimateDiverges(estimatedInputTokensCount: 100, actualInputTokensCount: 105);

        _logger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}

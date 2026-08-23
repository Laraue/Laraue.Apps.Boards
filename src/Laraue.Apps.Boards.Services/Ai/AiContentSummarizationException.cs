namespace Laraue.Apps.Boards.Services.Ai;

/// <summary>
/// Thrown when an <see cref="IAiContentSummarizer"/> implementation fails to produce a summary.
/// </summary>
public class AiContentSummarizationException : Exception
{
    public AiContentSummarizationException(string message)
        : base(message)
    {
    }

    public AiContentSummarizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

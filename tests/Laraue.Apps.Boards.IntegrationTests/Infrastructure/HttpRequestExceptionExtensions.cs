using Xunit.Sdk;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public static class HttpRequestExceptionExtensions
{
    public static T HasInnerException<T>(this HttpRequestException exception) where T : Exception
    {
        if (exception.InnerException is null)
            throw new InvalidOperationException("No inner exception thrown.", exception);

        try
        {
            return Assert.IsType<T>(exception.InnerException);
        }
        catch (XunitException)
        {
            throw new Exception(
                $"Exception of type {typeof(T)} excepted, but {exception.InnerException.GetType()} was thrown",
                exception.InnerException);
        }
    }
}
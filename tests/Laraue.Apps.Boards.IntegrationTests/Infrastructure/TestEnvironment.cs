using System.Runtime.CompilerServices;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

/// <summary>
/// Process-wide settings for every test host, applied before any test runs.
/// </summary>
internal static class TestEnvironment
{
    public const string ReloadConfigOnChangeVariable = "DOTNET_hostBuilder__reloadConfigOnChange";

    /// <summary>
    /// Every host built by the tests (WebApplicationFactory hosts, the bare WebApplication of the Telegram tests)
    /// would otherwise watch its appsettings files for changes. On Linux each watcher takes an inotify instance, the
    /// CI runner allows 128 per user, and the suite builds more hosts than that - tests then failed to start with
    /// "The configured user limit (128) on the number of inotify instances has been reached" (BRD-295). Config never
    /// changes during a test run, so nothing is lost.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(ReloadConfigOnChangeVariable, "false");
    }
}

using System.Runtime.CompilerServices;

namespace RegReturns.IntegrationTests.Infrastructure;

/// <summary>
/// Makes every test host watch files by polling. Each host otherwise opens inotify instances for its configuration
/// files and web root, and the suite's hosts together exceed Linux's default limit of 128 per user; past it, pages that
/// version a static file fail with "The configured user limit (128) on the number of inotify instances has been
/// reached".
/// </summary>
internal static class PollingFileWatchers
{
    [ModuleInitializer]
    internal static void Enable() => Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
}

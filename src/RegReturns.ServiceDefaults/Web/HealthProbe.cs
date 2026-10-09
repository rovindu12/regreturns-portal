using System.Globalization;

namespace RegReturns.ServiceDefaults.Web;

/// <summary>
/// The container health check (ADR 0034). The runtime images have no shell, curl or wget, so the image's
/// <c>HEALTHCHECK</c> starts the app's own binary with <see cref="Argument"/>: it asks the running instance for a health
/// endpoint over loopback and exits 0 if the answer is a success, 1 otherwise, without building the host.
/// </summary>
public static class HealthProbe
{
    /// <summary>The first command-line argument that turns a start into a probe.</summary>
    public const string Argument = "--health-probe";

    /// <summary>The port the ASP.NET Core images listen on when <c>ASPNETCORE_HTTP_PORTS</c> is not set.</summary>
    public const int DefaultPort = 8080;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Gets a value indicating whether the command line asks for a probe.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns><see langword="true"/> if the first argument is <see cref="Argument"/>.</returns>
    public static bool IsRequested(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Count > 0 && string.Equals(args[0], Argument, StringComparison.Ordinal);
    }

    /// <summary>
    /// Calls the endpoint named by the second argument (<see cref="WebDefaultsExtensions.LivePath"/> by default) on the
    /// first port of <c>ASPNETCORE_HTTP_PORTS</c> and prints one line with the outcome, which Docker keeps with the
    /// container's health state.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>0 when healthy, 1 otherwise.</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var target = Target(Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), args.Count > 1 ? args[1] : null);
        using var client = new HttpClient { Timeout = Timeout };
        try
        {
            using var response = await client.GetAsync(target);
            await Console.Out.WriteLineAsync($"{target.AbsolutePath}: HTTP {(int)response.StatusCode}");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Console.Out.WriteLineAsync($"{target.AbsolutePath}: {ex.GetType().Name}");
            return 1;
        }
    }

    /// <summary>Returns the loopback address to call.</summary>
    /// <param name="httpPorts">The value of <c>ASPNETCORE_HTTP_PORTS</c> (for example <c>8080</c> or <c>8080;8081</c>).</param>
    /// <param name="path">The endpoint path, or <see langword="null"/> for the liveness endpoint.</param>
    /// <returns>The address.</returns>
    /// <exception cref="ArgumentException">The path is not an absolute path on this host.</exception>
    public static Uri Target(string? httpPorts, string? path)
    {
        var first = httpPorts?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var port = int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed is > 0 and <= 65535
            ? parsed
            : DefaultPort;
        var endpoint = string.IsNullOrWhiteSpace(path) ? WebDefaultsExtensions.LivePath : path;
        if (!endpoint.StartsWith('/') || endpoint.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The probe path must start with a single '/', for example {WebDefaultsExtensions.ReadyPath}.", nameof(path));
        }

        return new Uri($"http://127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}{endpoint}");
    }
}

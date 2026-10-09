using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using RegReturns.Application.Demo;
using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Ai;
using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.ServiceDefaults;

namespace RegReturns.Web.Status;

/// <summary>One health check as the diagnostics page shows it.</summary>
/// <param name="Name">The check's name.</param>
/// <param name="Tags">Its tags (<c>live</c>, <c>ready</c>).</param>
/// <param name="Status">Its result.</param>
/// <param name="DurationMs">How long it took.</param>
/// <param name="Description">What it reported.</param>
/// <param name="Error">The exception type and message, if it failed with one.</param>
public sealed record HealthCheckLine(string Name, string Tags, HealthStatus Status, double DurationMs, string? Description, string? Error);

/// <summary>A setting as the diagnostics page shows it: never a secret, only whether one is set.</summary>
/// <param name="Name">The configuration key.</param>
/// <param name="Value">Its value, or whether it is set.</param>
public sealed record SettingLine(string Name, string Value);

/// <summary>The host side of the diagnostics page.</summary>
/// <param name="Version">The build's version and commit.</param>
/// <param name="Environment">The hosting environment.</param>
/// <param name="StartedAt">When the process started.</param>
/// <param name="Uptime">How long it has run.</param>
/// <param name="Runtime">.NET, the operating system and the processor architecture.</param>
/// <param name="Processors">Logical processors.</param>
/// <param name="WorkingSetMb">The process's memory.</param>
/// <param name="GcHeapMb">Managed heap size.</param>
/// <param name="GcMode">Server or workstation garbage collection.</param>
/// <param name="Health">Every health check, live and ready.</param>
/// <param name="Settings">Settings that explain behaviour, without secrets.</param>
/// <param name="CheckedAt">When this was read.</param>
public sealed record HostDiagnostics(
    string Version,
    string Environment,
    DateTimeOffset StartedAt,
    TimeSpan Uptime,
    string Runtime,
    int Processors,
    long WorkingSetMb,
    long GcHeapMb,
    string GcMode,
    IReadOnlyList<HealthCheckLine> Health,
    IReadOnlyList<SettingLine> Settings,
    DateTimeOffset CheckedAt);

/// <summary>
/// Gathers the host side of the administrator's diagnostics page (ADR 0033): build, runtime, every health check with
/// its timing and error, and the settings that change behaviour. Secrets are reported only as set or not set, and the
/// page needs an administrator with two-step sign-in, so the checks run on each visit without a cache.
/// </summary>
/// <param name="health">The health checks.</param>
/// <param name="environment">The hosting environment.</param>
/// <param name="configuration">Configuration, for settings without an options class.</param>
/// <param name="wso2">WSO2's addresses.</param>
/// <param name="provisioner">The provisioner client.</param>
/// <param name="enrolment">TOTP enrolment windows.</param>
/// <param name="demo">Demo mode.</param>
/// <param name="ai">Advisory insights.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class PortalDiagnostics(
    HealthCheckService health,
    IHostEnvironment environment,
    IConfiguration configuration,
    IOptions<Wso2Options> wso2,
    IOptions<Wso2ProvisionerOptions> provisioner,
    IOptions<TotpEnrolmentOptions> enrolment,
    IOptions<DemoOptions> demo,
    IOptions<AiOptions> ai,
    TimeProvider timeProvider)
{
    private const string Set = "set";
    private const string NotSet = "not set";
    private const int MaxErrorLength = 300;

    /// <summary>Runs the health checks and reads the host's facts.</summary>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>The host diagnostics.</returns>
    public async Task<HostDiagnostics> GetAsync(CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(cancellationToken);
        using var process = Process.GetCurrentProcess();
        var now = timeProvider.GetUtcNow();
        var startedAt = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        return new HostDiagnostics(
            BuildInfo.Version,
            environment.EnvironmentName,
            startedAt,
            now - startedAt,
            $"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})",
            System.Environment.ProcessorCount,
            process.WorkingSet64 / (1024 * 1024),
            GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024),
            GCSettings.IsServerGC ? "server" : "workstation",
            [.. report.Entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => Line(e.Key, e.Value))],
            Settings(),
            now);
    }

    /// <summary>Turns a health entry into a line, with the error cut to a readable length.</summary>
    /// <param name="name">The check's name.</param>
    /// <param name="entry">Its result.</param>
    /// <returns>The line.</returns>
    public static HealthCheckLine Line(string name, HealthReportEntry entry)
    {
        var error = entry.Exception is { } ex ? $"{ex.GetType().Name}: {ex.Message}" : null;
        if (error is { Length: > MaxErrorLength })
        {
            error = string.Concat(error.AsSpan(0, MaxErrorLength), "…");
        }

        return new HealthCheckLine(name, string.Join(", ", entry.Tags.Order(StringComparer.Ordinal)), entry.Status, entry.Duration.TotalMilliseconds, entry.Description, error);
    }

    // Scheme, host and port only: a URL's path or user info is no business of this page.
    private static string? OriginOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : Set;
    }

    private List<SettingLine> Settings()
    {
        var identity = wso2.Value;
        var insights = ai.Value;
        var otlp = configuration["Observability:OtlpEndpoint"] ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        return
        [
            new("Wso2:Authority", identity.Authority?.ToString() ?? NotSet),
            new("Wso2:BackchannelAuthority", identity.BackchannelAuthority?.ToString() ?? "(the authority)"),
            new("Wso2:TrustedCaPath", string.IsNullOrWhiteSpace(identity.TrustedCaPath) ? "not set (system roots)" : Set),
            new("Iam:Provisioner", provisioner.Value.IsConfigured ? "client id and secret set" : NotSet),
            new("Iam:TotpEnrolment:WindowHours", enrolment.Value.WindowHours.ToString(CultureInfo.InvariantCulture)),
            new("Demo:Enabled", demo.Value.Enabled ? "true" : "false"),
            new("Demo:ResetSchedule", string.IsNullOrWhiteSpace(demo.Value.ResetSchedule) ? "off" : $"{demo.Value.ResetSchedule} (UTC)"),
            new("Ai:Provider", insights.Provider.ToString()),
            new("Ai:Anthropic:ApiKey", string.IsNullOrWhiteSpace(insights.Anthropic.ApiKey) ? "not set (rule-based insights)" : Set),
            new("Ai:Anthropic:Model", insights.Anthropic.Model),
            new("Serilog:MinimumLevel:Default", configuration["Serilog:MinimumLevel:Default"] ?? "Information"),
            new("Observability:OtlpEndpoint", OriginOf(otlp) ?? "not set (no traces exported)"),
        ];
    }
}

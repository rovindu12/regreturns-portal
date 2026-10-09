using System.Globalization;

using RegReturns.Domain.Common;

namespace RegReturns.Application.Demo;

/// <summary>Why a demo reset was refused (ADR 0031).</summary>
public static class DemoErrors
{
    /// <summary>Demo mode is off.</summary>
    public static readonly Error Disabled = new("Demo.Disabled", "This is not a demo deployment, so it cannot be reset.");

    /// <summary>Only a system administrator may reset the demo; the scheduled reset acts as the system.</summary>
    public static readonly Error AdminsOnly = new("Demo.AdminsOnly", "Only a system administrator can reset the demo.");

    /// <summary>The latest reset was too recent.</summary>
    public static readonly Error CoolingDown = new("Demo.CoolingDown", "The demo was reset a few minutes ago. Try again later.");

    /// <summary>Another reset holds the lock.</summary>
    public static readonly Error InProgress = new("Demo.InProgress", "A demo reset is already running. Wait for it to finish.");

    /// <summary>A person in the directory is not a demo account, so the data may be real.</summary>
    public static readonly Error NotADemoDatabase = new(
        "Demo.NotADemoDatabase",
        "The directory holds people who are not demo accounts, so this database is never reset.");

    /// <summary>Returns <see cref="CoolingDown"/> saying when a reset will be accepted again.</summary>
    /// <param name="availableAt">When the cooldown ends (UTC).</param>
    /// <returns>The error.</returns>
    public static Error CoolingDownUntil(DateTimeOffset availableAt) => CoolingDown.WithMessage(string.Create(
        CultureInfo.InvariantCulture,
        $"The demo was reset a few minutes ago. It can be reset again from {availableAt.UtcDateTime:HH:mm} UTC."));

    /// <summary>When a manual reset is accepted again after the latest one.</summary>
    /// <param name="lastResetAt">When the latest reset happened, if ever.</param>
    /// <param name="cooldown">The cooldown.</param>
    /// <returns>The first moment a manual reset is accepted, or <see langword="null"/> when there was none.</returns>
    public static DateTimeOffset? AvailableAt(DateTimeOffset? lastResetAt, TimeSpan cooldown) => lastResetAt + cooldown;
}

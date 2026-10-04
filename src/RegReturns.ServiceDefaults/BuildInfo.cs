using System.Reflection;

namespace RegReturns.ServiceDefaults;

/// <summary>Version information stamped into the running assembly at build time.</summary>
public static class BuildInfo
{
    /// <summary>Gets the informational version, including the git commit when built from source control (for example <c>0.1.0+3f2a1c9</c>).</summary>
    public static string Version { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}

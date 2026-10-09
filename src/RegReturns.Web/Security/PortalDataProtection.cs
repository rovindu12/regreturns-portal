using Microsoft.AspNetCore.DataProtection;

namespace RegReturns.Web.Security;

/// <summary>Registers ASP.NET Core data protection for the portal.</summary>
public static class PortalDataProtection
{
    /// <summary>The application name that isolates the portal's keys and payloads from any other app.</summary>
    public const string ApplicationName = "RegReturns.Web";

    /// <summary>
    /// Adds data protection under a fixed application name (so a payload stays readable whatever folder the app runs
    /// from), persisting the keys to <see cref="PortalDataProtectionOptions.KeysPath"/> when it is set.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddPortalDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var settings = configuration.GetSection(PortalDataProtectionOptions.SectionName).Get<PortalDataProtectionOptions>()
            ?? new PortalDataProtectionOptions();
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (!string.IsNullOrWhiteSpace(settings.KeysPath))
        {
            // The keys are stored unencrypted: Linux has no machine key store, and the volume is readable only by the
            // portal's user and root on the host. They protect cookies only, never data at rest.
            builder.PersistKeysToFileSystem(new DirectoryInfo(settings.KeysPath));
        }

        return services;
    }
}

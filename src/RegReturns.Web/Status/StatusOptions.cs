namespace RegReturns.Web.Status;

/// <summary>Settings of the public status page (section <c>Status</c>).</summary>
public sealed class StatusOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Status";

    /// <summary>
    /// Gets or sets the REST API's readiness endpoint as the portal reaches it, such as
    /// <c>http://api:8080/health/ready</c> on the stack's internal network (ADR 0034). Unset: the page does not list the API.
    /// </summary>
    public Uri? ApiHealthUrl { get; set; }

    /// <summary>Checks that <see cref="ApiHealthUrl"/>, when set, is an absolute HTTP or HTTPS URL.</summary>
    /// <returns>Whether the settings are valid.</returns>
    public bool AreValid() =>
        ApiHealthUrl is null || (ApiHealthUrl.IsAbsoluteUri && (ApiHealthUrl.Scheme == Uri.UriSchemeHttp || ApiHealthUrl.Scheme == Uri.UriSchemeHttps));
}

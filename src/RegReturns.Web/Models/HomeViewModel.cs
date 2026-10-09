using RegReturns.Application.Portal;

namespace RegReturns.Web.Models;

/// <summary>Data for the public landing page.</summary>
/// <param name="Summary">Headline counts.</param>
/// <param name="DemoEnabled">Whether this is the public demo, which adds the links to the demo accounts and the guide.</param>
/// <param name="SwaggerUrl">The API's Swagger UI, if the API address is configured.</param>
public sealed record HomeViewModel(PortalSummary Summary, bool DemoEnabled, Uri? SwaggerUrl);

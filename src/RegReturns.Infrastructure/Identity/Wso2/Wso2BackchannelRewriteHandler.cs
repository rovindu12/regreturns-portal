namespace RegReturns.Infrastructure.Identity.Wso2;

/// <summary>
/// Sends server-to-server requests for WSO2's public URLs to its internal address instead (for example
/// <c>https://iam.example.org</c> to <c>https://wso2:9443</c>), so issuer and discovery URLs stay public.
/// </summary>
/// <param name="publicAuthority">The public base URL of WSO2.</param>
/// <param name="backchannelAuthority">The internal base URL of WSO2.</param>
public sealed class Wso2BackchannelRewriteHandler(Uri publicAuthority, Uri backchannelAuthority) : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is { IsAbsoluteUri: true } uri && IsSameOrigin(uri, publicAuthority) && !IsSameOrigin(uri, backchannelAuthority))
        {
            request.RequestUri = new UriBuilder(uri)
            {
                Scheme = backchannelAuthority.Scheme,
                Host = backchannelAuthority.Host,
                Port = backchannelAuthority.Port,
            }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }

    private static bool IsSameOrigin(Uri left, Uri right) =>
        Uri.Compare(left, right, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}

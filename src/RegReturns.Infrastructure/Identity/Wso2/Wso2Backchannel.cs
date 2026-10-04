using System.Net;

namespace RegReturns.Infrastructure.Identity.Wso2;

/// <summary>Builds the HTTP pipeline used for every server-to-server call to WSO2.</summary>
public static class Wso2Backchannel
{
    /// <summary>Name of the <see cref="HttpClient"/> registered for WSO2 calls.</summary>
    public const string HttpClientName = "wso2-backchannel";

    /// <summary>Creates the primary handler, trusting only the configured CA when one is set.</summary>
    /// <param name="options">The WSO2 options.</param>
    /// <returns>A new handler.</returns>
    public static SocketsHttpHandler CreatePrimaryHandler(Wso2Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };

        if (!string.IsNullOrWhiteSpace(options.TrustedCaPath))
        {
            var validator = Wso2CertificateValidator.FromPemFile(options.TrustedCaPath);
            handler.SslOptions.RemoteCertificateValidationCallback =
                (_, certificate, chain, errors) => validator.Validate(certificate, chain, errors);
        }

        return handler;
    }

    /// <summary>Creates the full pipeline (rewrite + primary handler) for components that take a handler instance.</summary>
    /// <param name="options">The WSO2 options.</param>
    /// <returns>A new handler pipeline.</returns>
    public static HttpMessageHandler CreatePipeline(Wso2Options options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Wso2BackchannelRewriteHandler(options.Authority!, options.EffectiveBackchannelAuthority)
        {
            InnerHandler = CreatePrimaryHandler(options),
        };
    }
}

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.Identity;

/// <summary>Real TLS handshakes on loopback against a server presenting certificates from the test PKI.</summary>
public sealed class Wso2BackchannelTests(TestCertificateAuthority pki) : IClassFixture<TestCertificateAuthority>
{
    [Fact]
    public async Task Backchannel_trusting_the_private_ca_accepts_a_server_that_sends_its_intermediate()
    {
        var caFile = WritePem(pki.Root);
        try
        {
            var status = await GetOverTlsAsync(caFile, pki.LocalhostLeafFromIntermediate, pki.Intermediate);

            status.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            File.Delete(caFile);
        }
    }

    [Fact]
    public async Task Backchannel_trusting_another_ca_refuses_the_connection()
    {
        var caFile = WritePem(pki.RogueRoot);
        try
        {
            await Should.ThrowAsync<HttpRequestException>(() =>
                GetOverTlsAsync(caFile, pki.LocalhostLeafFromIntermediate, pki.Intermediate));
        }
        finally
        {
            File.Delete(caFile);
        }
    }

    [Fact]
    public async Task Backchannel_without_a_configured_ca_does_not_trust_the_private_ca()
    {
        await Should.ThrowAsync<HttpRequestException>(() =>
            GetOverTlsAsync(trustedCaPath: null, pki.LocalhostLeafFromIntermediate, pki.Intermediate));
    }

    [Fact]
    public void Primary_handler_leaves_validation_to_the_platform_when_no_ca_is_configured()
    {
        using var handler = Wso2Backchannel.CreatePrimaryHandler(new Wso2Options { Authority = new Uri("https://iam.valoria.test/") });

        handler.SslOptions.RemoteCertificateValidationCallback.ShouldBeNull();
    }

    [Fact]
    public void Pipeline_rewrites_requests_before_the_primary_handler()
    {
        using var pipeline = Wso2Backchannel.CreatePipeline(new Wso2Options { Authority = new Uri("https://iam.valoria.test/") });

        pipeline.ShouldBeOfType<Wso2BackchannelRewriteHandler>().InnerHandler.ShouldBeOfType<SocketsHttpHandler>();
    }

    private static async Task<HttpStatusCode> GetOverTlsAsync(string? trustedCaPath, X509Certificate2 serverCertificate, params X509Certificate2[] intermediates)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = ServeOnceAsync(listener, serverCertificate, intermediates, timeout.Token);
        try
        {
            var options = new Wso2Options { Authority = new Uri($"https://localhost:{port}/"), TrustedCaPath = trustedCaPath };
            using var client = new HttpClient(Wso2Backchannel.CreatePrimaryHandler(options));
            using var response = await client.GetAsync(options.JwksAddress, timeout.Token);
            return response.StatusCode;
        }
        finally
        {
            listener.Stop();
            await server;
        }
    }

    private static async Task ServeOnceAsync(TcpListener listener, X509Certificate2 certificate, X509Certificate2[] intermediates, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var tls = new SslStream(connection.GetStream());
            await tls.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificateContext = SslStreamCertificateContext.Create(certificate, [.. intermediates], offline: true),
                },
                cancellationToken);

            var buffer = new byte[8192];
            var received = 0;
            while (!Encoding.ASCII.GetString(buffer, 0, received).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                var read = await tls.ReadAsync(buffer.AsMemory(received), cancellationToken);
                if (read == 0)
                {
                    return;
                }

                received += read;
            }

            await tls.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), cancellationToken);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client refused the certificate or went away; the test asserts on the client side.
        }
    }

    private static string WritePem(X509Certificate2 certificate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"regreturns-ca-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }
}

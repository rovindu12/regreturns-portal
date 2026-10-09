using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Infrastructure.Persistence;

using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(RegReturns.IntegrationTests.Infrastructure.SqlServerFixture))]

namespace RegReturns.IntegrationTests.Infrastructure;

/// <summary>
/// Starts one SQL Server container for the whole test run, applies migrations and loads the demo data once.
/// Tests that change data must use their own database (see <see cref="NewDatabaseConnectionString"/>). As in
/// docker-compose.yml (ADR 0033), the server forces TLS with a certificate made for the run, and every connection uses
/// strict encryption pinned to that certificate, so the apps are tested the way they connect for real.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>The anchor date for demo data, so assertions are stable whatever day the tests run.</summary>
    public static readonly DateTimeOffset SeedDate = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The SQL Server image used by the tests. Keep it in step with the <c>sqlserver</c> service in
    /// <c>docker-compose.yml</c>; Dependabot only bumps the compose file.
    /// </summary>
    public const string Image = "mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04";

    private const string MssqlConf = """
        [network]
        tlscert = /var/opt/mssql/tls/mssql.crt
        tlskey = /var/opt/mssql/tls/mssql.key
        forceencryption = 1
        """;

    private readonly string _certificatePath = Path.Combine(Path.GetTempPath(), $"regreturns-it-mssql-{Guid.NewGuid():N}.crt");
    private readonly MsSqlContainer _container;

    public SqlServerFixture()
    {
        var (certificate, key) = CreateCertificate();
        File.WriteAllText(_certificatePath, certificate);
        _container = new MsSqlBuilder(Image)
            .WithResourceMapping(Encoding.ASCII.GetBytes(certificate), "/var/opt/mssql/tls/mssql.crt")
            .WithResourceMapping(Encoding.ASCII.GetBytes(key), "/var/opt/mssql/tls/mssql.key")
            .WithResourceMapping(Encoding.ASCII.GetBytes(MssqlConf), "/var/opt/mssql/mssql.conf")
            .Build();
    }

    /// <summary>Gets the connection string of the seeded shared database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = DatabaseConnectionString("RegReturns");

        await using var context = CreateContext(ConnectionString);
        var initializer = new DatabaseInitializer(context, new FakeTimeProvider(SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(CancellationToken.None);
        await initializer.SeedAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        File.Delete(_certificatePath);
    }

    /// <summary>Returns a connection string for a new, not-yet-created database on the same server.</summary>
    public string NewDatabaseConnectionString() => DatabaseConnectionString($"RegReturns_{Guid.NewGuid():N}");

    public static RegReturnsDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RegReturnsDbContext>().UseSqlServer(connectionString, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Options);

    private string DatabaseConnectionString(string database) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = database,
            Encrypt = SqlConnectionEncryptOption.Strict,
            TrustServerCertificate = false,
            ServerCertificate = _certificatePath,
        }.ConnectionString;

    // A throwaway self-signed certificate: the connection strings pin it, so no CA is needed.
    private static (string Certificate, string Key) CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return (certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem());
    }
}

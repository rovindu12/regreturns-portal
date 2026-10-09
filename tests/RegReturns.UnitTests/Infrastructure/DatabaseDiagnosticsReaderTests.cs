using RegReturns.Infrastructure.Persistence;

namespace RegReturns.UnitTests.Infrastructure;

public sealed class DatabaseDiagnosticsReaderTests
{
    [Theory]
    [InlineData("Server=db;Encrypt=Strict;ServerCertificate=/certs/mssql.crt;Password=p", "Encrypt=Strict, certificate pinned")]
    [InlineData("Server=db;Encrypt=True;TrustServerCertificate=True;Password=p", "Encrypt=True, certificate NOT validated")]
    [InlineData("Server=db;Password=p", "Encrypt=True, certificate validated by the system's trusted roots")]
    public void The_client_side_says_how_the_certificate_is_checked_and_nothing_else(string connectionString, string expected)
    {
        var description = DatabaseDiagnosticsReader.ClientEncryption(connectionString);

        description.ShouldBe(expected);
        description.ShouldNotContain("Password");
    }
}

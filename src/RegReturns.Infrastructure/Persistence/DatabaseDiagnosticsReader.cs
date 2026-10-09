using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Diagnostics;

namespace RegReturns.Infrastructure.Persistence;

/// <summary>
/// Reads <see cref="DatabaseDiagnostics"/> over the app's own connection: migrations from EF Core, the server's version,
/// and how the connection is encrypted, both as SQL Server reports it and as the connection settings demand it. The
/// connection string's password never leaves this class.
/// </summary>
/// <param name="context">The EF Core context.</param>
public sealed class DatabaseDiagnosticsReader(RegReturnsDbContext context) : IDatabaseDiagnostics
{
    /// <inheritdoc />
    public async Task<DatabaseDiagnostics> GetAsync(CancellationToken cancellationToken)
    {
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var server = await context.Database
            .SqlQueryRaw<ServerRow>("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) + N' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128)) AS Value")
            .SingleAsync(cancellationToken);
        return new DatabaseDiagnostics(
            context.Database.GetDbConnection().Database,
            server.Value,
            await EncryptionAsync(cancellationToken),
            ClientEncryption(context.Database.GetConnectionString()),
            applied.Count,
            applied.LastOrDefault(),
            pending);
    }

    /// <summary>Describes how a connection string asks for encryption, without any credential.</summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>For example <c>Encrypt=Strict, certificate pinned</c>.</returns>
    public static string ClientEncryption(string? connectionString)
    {
        var settings = new SqlConnectionStringBuilder(connectionString);
        var check = settings switch
        {
            { ServerCertificate: { Length: > 0 } } => "certificate pinned",
            { TrustServerCertificate: true } => "certificate NOT validated",
            _ => "certificate validated by the system's trusted roots",
        };
        return $"Encrypt={settings.Encrypt}, {check}";
    }

    // sys.dm_exec_connections needs VIEW SERVER PERFORMANCE STATE; a login without it simply gets no answer here.
    private async Task<string?> EncryptionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var row = await context.Database
                .SqlQueryRaw<ServerRow>(
                    "SELECT CASE WHEN encrypt_option = 'TRUE' THEN N'encrypted, TDS ' + CONVERT(nvarchar(10), protocol_version / 16777216) ELSE N'NOT encrypted' END AS Value " +
                    "FROM sys.dm_exec_connections WHERE session_id = @@SPID")
                .SingleOrDefaultAsync(cancellationToken);
            return row?.Value;
        }
        catch (SqlException)
        {
            return null;
        }
    }

    private sealed record ServerRow(string Value);
}

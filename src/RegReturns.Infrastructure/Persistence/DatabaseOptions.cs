using System.ComponentModel.DataAnnotations;

namespace RegReturns.Infrastructure.Persistence;

/// <summary>Database settings. The connection string comes from <c>ConnectionStrings:RegReturns</c>.</summary>
public sealed class DatabaseOptions
{
    /// <summary>The name of the connection string in configuration.</summary>
    public const string ConnectionStringName = "RegReturns";

    /// <summary>Gets or sets the SQL Server connection string. Supply it through user-secrets or environment variables.</summary>
    [Required(ErrorMessage = "ConnectionStrings:RegReturns is not set. Use 'dotnet user-secrets' locally or the ConnectionStrings__RegReturns environment variable.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the command timeout in seconds.</summary>
    [Range(5, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets the maximum number of retries for transient SQL errors.</summary>
    [Range(0, 10)]
    public int MaxRetryCount { get; set; } = 5;
}

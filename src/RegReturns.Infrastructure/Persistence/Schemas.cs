namespace RegReturns.Infrastructure.Persistence;

/// <summary>Database schema names, grouping tables by area.</summary>
internal static class Schemas
{
    /// <summary>Institutions, return types and templates.</summary>
    public const string Reference = "reference";

    /// <summary>The user directory projection.</summary>
    public const string Identity = "iam";

    /// <summary>Obligations, submissions and their workflow.</summary>
    public const string Returns = "returns";

    /// <summary>The tamper-evident audit chain.</summary>
    public const string Audit = "audit";

    /// <summary>API plumbing: stored responses of idempotent requests.</summary>
    public const string Api = "api";

    /// <summary>Read-only views behind the dashboards and reports (ADR 0028).</summary>
    public const string Reporting = "reporting";

    /// <summary>Legacy migration runs and their row errors (ADR 0029).</summary>
    public const string Migration = "migration";
}

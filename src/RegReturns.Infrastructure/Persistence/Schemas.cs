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
}

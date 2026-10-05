using System.Data;
using System.Text;

using Dapper;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

using RegReturns.Application.Reporting;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Reporting;

/// <summary>
/// Reads the reporting views with Dapper (ADR 0028). Each query is plain SQL with parameters only, and its WHERE clause
/// holds only the conditions the filter sets, so SQL Server plans each shape once. Rows come back in the SQL types
/// (dates as <see cref="DateTime"/>, enums as their stored names) and are converted here, so the mapping never depends
/// on Dapper's handling of <see cref="DateOnly"/> or enums. Connections retry transient errors on open.
/// </summary>
/// <param name="options">The database settings.</param>
internal sealed class ReportingReadModel(IOptions<DatabaseOptions> options) : IReportingReadModel
{
    private const string ActiveInstitutions = "[InstitutionIsActive] = 1";

    private readonly DatabaseOptions _database = options.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObligationRow>> GetObligationsAsync(ObligationQuery filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var parameters = new DynamicParameters();
        var where = new List<string> { ActiveInstitutions };
        AddInstitution(where, parameters, filter.InstitutionId);
        if (filter.ReturnTypeCode is not null)
        {
            where.Add("[ReturnTypeCode] = @ReturnTypeCode");
            parameters.Add("ReturnTypeCode", filter.ReturnTypeCode, DbType.String);
        }

        if (filter.Window is not null)
        {
            AddWindow(where, parameters, filter.Window);
        }

        if (filter.OpenDueBefore is { } dueBefore)
        {
            where.Add($"[Status] = N'{nameof(ObligationStatus.Open)}' AND [DueDate] < @DueBefore");
            parameters.Add("DueBefore", dueBefore.ToDateTime(TimeOnly.MinValue), DbType.Date);
        }

        var sql = Select(
            """
            [ObligationId], [InstitutionId], [InstitutionCode], [InstitutionName], [ReturnTypeCode],
            [PeriodFrequency], [PeriodYear], [PeriodNumber], [DueDate], [Status], [FirstSubmittedAt], [IsLate],
            [SubmissionStatus], [SubmissionFirstSubmittedAt]
            """,
            "[reporting].[ObligationCompliance]",
            where,
            orderBy: "[InstitutionCode], [ReturnTypeCode], [PeriodFrequency], [PeriodKey]");
        var rows = await QueryAsync<ObligationData>(sql, parameters, cancellationToken);
        return [.. rows.Select(r => r.ToRow())];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FindingCountRow>> GetFindingCountsAsync(FindingQuery filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var parameters = new DynamicParameters();
        var where = new List<string> { ActiveInstitutions, "[ReturnTypeCode] = @ReturnTypeCode" };
        parameters.Add("ReturnTypeCode", filter.ReturnTypeCode, DbType.String);
        AddInstitution(where, parameters, filter.InstitutionId);
        AddWindow(where, parameters, filter.Window);

        var sql = Select(
            "[PeriodYear], [PeriodNumber], [RuleCode], [Severity], COUNT(*) AS [Findings], COUNT(DISTINCT [SubmissionId]) AS [Returns]",
            "[reporting].[SubmittedFindings]",
            where,
            orderBy: "[PeriodYear], [PeriodNumber], [RuleCode], [Severity]",
            groupBy: "[PeriodYear], [PeriodNumber], [RuleCode], [Severity]");
        var rows = await QueryAsync<FindingCountData>(sql, parameters, cancellationToken);
        return [.. rows.Select(r => r.ToRow(filter.Window.Frequency))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovedValueRow>> GetApprovedValuesAsync(ApprovedValueQuery filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.FieldCodes.Count == 0)
        {
            return [];
        }

        var parameters = new DynamicParameters();
        var where = new List<string> { ActiveInstitutions, "[ReturnTypeCode] = @ReturnTypeCode", "[FieldCode] IN @FieldCodes" };
        parameters.Add("ReturnTypeCode", filter.ReturnTypeCode, DbType.String);
        parameters.Add("FieldCodes", filter.FieldCodes);
        AddInstitution(where, parameters, filter.InstitutionId);
        AddWindow(where, parameters, filter.Window);

        var sql = Select(
            "[InstitutionId], [InstitutionCode], [InstitutionName], [PeriodYear], [PeriodNumber], [FieldCode], [Value]",
            "[reporting].[ApprovedValues]",
            where,
            orderBy: "[InstitutionCode], [PeriodKey], [FieldCode]");
        var rows = await QueryAsync<ApprovedValueData>(sql, parameters, cancellationToken);
        return [.. rows.Select(r => r.ToRow(filter.Window.Frequency))];
    }

    private static void AddInstitution(List<string> where, DynamicParameters parameters, Guid? institutionId)
    {
        if (institutionId is { } id)
        {
            where.Add("[InstitutionId] = @InstitutionId");
            parameters.Add("InstitutionId", id, DbType.Guid);
        }
    }

    private static void AddWindow(List<string> where, DynamicParameters parameters, PeriodWindow window)
    {
        where.Add("[PeriodFrequency] = @Frequency AND [PeriodKey] BETWEEN @FromKey AND @ToKey");
        parameters.Add("Frequency", window.Frequency.ToString(), DbType.String);
        parameters.Add("FromKey", KeyOf(window.From), DbType.Int32);
        parameters.Add("ToKey", KeyOf(window.To), DbType.Int32);
    }

    /// <summary>The view's <c>PeriodKey</c>: year * 100 + month or quarter.</summary>
    private static int KeyOf(ReportingPeriod period) => (period.Year * 100) + period.Number;

    private static string Select(string columns, string view, List<string> where, string orderBy, string? groupBy = null)
    {
        var sql = new StringBuilder()
            .Append("SELECT ").AppendLine(columns)
            .Append("FROM ").AppendLine(view)
            .Append("WHERE ").AppendJoin(" AND ", where.Select(c => $"({c})")).AppendLine();
        if (groupBy is not null)
        {
            sql.Append("GROUP BY ").AppendLine(groupBy);
        }

        return sql.Append("ORDER BY ").Append(orderBy).Append(';').ToString();
    }

    private static ReportingPeriod PeriodOf(ReturnFrequency frequency, int year, int number) =>
        frequency == ReturnFrequency.Monthly ? ReportingPeriod.Monthly(year, number) : ReportingPeriod.Quarterly(year, number);

    private static TEnum Parse<TEnum>(string value)
        where TEnum : struct, Enum => Enum.Parse<TEnum>(value, ignoreCase: false);

    private async Task<IEnumerable<T>> QueryAsync<T>(string sql, DynamicParameters parameters, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QueryAsync<T>(new CommandDefinition(
            sql, parameters, commandTimeout: _database.CommandTimeoutSeconds, cancellationToken: cancellationToken));
    }

    private SqlConnection CreateConnection() => new(_database.ConnectionString)
    {
        RetryLogicProvider = _database.MaxRetryCount == 0
            ? SqlConfigurableRetryFactory.CreateNoneRetryProvider()
            : SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
            {
                NumberOfTries = _database.MaxRetryCount + 1,
                DeltaTime = TimeSpan.FromSeconds(1),
                MaxTimeInterval = TimeSpan.FromSeconds(20),
            }),
    };

    // Dapper fills these through their constructors, matching columns by name and SQL types exactly
    // (uniqueidentifier, nvarchar, int, date as DateTime, datetimeoffset, bit, decimal).
    private sealed record ObligationData(
        Guid ObligationId,
        Guid InstitutionId,
        string InstitutionCode,
        string InstitutionName,
        string ReturnTypeCode,
        string PeriodFrequency,
        int PeriodYear,
        int PeriodNumber,
        DateTime DueDate,
        string Status,
        DateTimeOffset? FirstSubmittedAt,
        bool IsLate,
        string? SubmissionStatus,
        DateTimeOffset? SubmissionFirstSubmittedAt)
    {
        public ObligationRow ToRow() => new(
            ObligationId,
            InstitutionId,
            InstitutionCode,
            InstitutionName,
            ReturnTypeCode,
            PeriodOf(Parse<ReturnFrequency>(PeriodFrequency), PeriodYear, PeriodNumber),
            DateOnly.FromDateTime(DueDate),
            Parse<ObligationStatus>(Status),
            FirstSubmittedAt,
            IsLate,
            SubmissionStatus is null ? null : Parse<SubmissionStatus>(SubmissionStatus),
            SubmissionFirstSubmittedAt);
    }

    private sealed record FindingCountData(int PeriodYear, int PeriodNumber, string RuleCode, string Severity, int Findings, int Returns)
    {
        public FindingCountRow ToRow(ReturnFrequency frequency) => new(
            PeriodOf(frequency, PeriodYear, PeriodNumber), RuleCode, Parse<Severity>(Severity), Findings, Returns);
    }

    private sealed record ApprovedValueData(
        Guid InstitutionId, string InstitutionCode, string InstitutionName, int PeriodYear, int PeriodNumber, string FieldCode, decimal Value)
    {
        public ApprovedValueRow ToRow(ReturnFrequency frequency) => new(
            InstitutionId, InstitutionCode, InstitutionName, PeriodOf(frequency, PeriodYear, PeriodNumber), FieldCode, Value);
    }
}

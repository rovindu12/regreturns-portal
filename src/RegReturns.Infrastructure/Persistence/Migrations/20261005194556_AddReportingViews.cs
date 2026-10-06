using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Read-only views behind the dashboards and reports (ADR 0028). The views are not part of the EF model: Dapper
    /// reads them through <c>ReportingReadModel</c>, whose integration tests query every column, so a later migration
    /// that renames a column they use fails those tests. <c>PeriodKey</c> is year * 100 + month or quarter, which sorts
    /// and filters periods of one frequency.
    /// </summary>
    public partial class AddReportingViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "reporting");

            // One row per obligation, with its live (not rejected) return if one was started. The unique filtered
            // index UX_Submissions_LiveObligation allows at most one, so the join never duplicates an obligation.
            migrationBuilder.Sql("""
                CREATE VIEW [reporting].[ObligationCompliance] AS
                SELECT
                    o.[Id] AS [ObligationId],
                    o.[InstitutionId],
                    i.[Code] AS [InstitutionCode],
                    i.[Name] AS [InstitutionName],
                    i.[IsActive] AS [InstitutionIsActive],
                    rt.[Code] AS [ReturnTypeCode],
                    o.[PeriodFrequency],
                    o.[PeriodYear],
                    o.[PeriodNumber],
                    o.[PeriodYear] * 100 + o.[PeriodNumber] AS [PeriodKey],
                    o.[DueDate],
                    o.[Status],
                    o.[FirstSubmittedAt],
                    o.[IsLate],
                    s.[Id] AS [SubmissionId],
                    s.[Status] AS [SubmissionStatus],
                    s.[FirstSubmittedAt] AS [SubmissionFirstSubmittedAt]
                FROM [returns].[Obligations] AS o
                INNER JOIN [reference].[Institutions] AS i ON i.[Id] = o.[InstitutionId]
                INNER JOIN [reference].[ReturnTypes] AS rt ON rt.[Id] = o.[ReturnTypeId]
                LEFT JOIN [returns].[Submissions] AS s ON s.[ObligationId] = o.[Id] AND s.[Status] <> N'Rejected';
                """);

            // Findings of revisions that reached the regulator: every revision before the current one was submitted
            // (only a submitted return can be sent back, which starts a new revision), and the current one counts once
            // its return is submitted. A bank's work in progress never shows.
            migrationBuilder.Sql("""
                CREATE VIEW [reporting].[SubmittedFindings] AS
                SELECT
                    s.[InstitutionId],
                    i.[IsActive] AS [InstitutionIsActive],
                    rt.[Code] AS [ReturnTypeCode],
                    o.[PeriodFrequency],
                    o.[PeriodYear],
                    o.[PeriodNumber],
                    o.[PeriodYear] * 100 + o.[PeriodNumber] AS [PeriodKey],
                    s.[Id] AS [SubmissionId],
                    f.[Revision],
                    f.[RuleCode],
                    f.[Severity]
                FROM [returns].[ValidationFindings] AS f
                INNER JOIN [returns].[Submissions] AS s ON s.[Id] = f.[SubmissionId]
                INNER JOIN [returns].[Obligations] AS o ON o.[Id] = s.[ObligationId]
                INNER JOIN [reference].[Institutions] AS i ON i.[Id] = s.[InstitutionId]
                INNER JOIN [reference].[ReturnTypes] AS rt ON rt.[Id] = s.[ReturnTypeId]
                WHERE f.[Revision] < s.[Revision]
                   OR s.[Status] IN (N'Submitted', N'UnderReview', N'Approved', N'Rejected');
                """);

            // Numeric values of approved returns, for key ratio trends.
            migrationBuilder.Sql("""
                CREATE VIEW [reporting].[ApprovedValues] AS
                SELECT
                    s.[InstitutionId],
                    i.[Code] AS [InstitutionCode],
                    i.[Name] AS [InstitutionName],
                    i.[IsActive] AS [InstitutionIsActive],
                    rt.[Code] AS [ReturnTypeCode],
                    o.[PeriodFrequency],
                    o.[PeriodYear],
                    o.[PeriodNumber],
                    o.[PeriodYear] * 100 + o.[PeriodNumber] AS [PeriodKey],
                    v.[FieldCode],
                    v.[NumericValue] AS [Value]
                FROM [returns].[SubmissionValues] AS v
                INNER JOIN [returns].[Submissions] AS s ON s.[Id] = v.[SubmissionId]
                INNER JOIN [returns].[Obligations] AS o ON o.[Id] = s.[ObligationId]
                INNER JOIN [reference].[Institutions] AS i ON i.[Id] = s.[InstitutionId]
                INNER JOIN [reference].[ReturnTypes] AS rt ON rt.[Id] = s.[ReturnTypeId]
                WHERE s.[Status] = N'Approved' AND v.[NumericValue] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS [reporting].[ApprovedValues];");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [reporting].[SubmittedFindings];");
            migrationBuilder.Sql("DROP VIEW IF EXISTS [reporting].[ObligationCompliance];");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS [reporting];");
        }
    }
}

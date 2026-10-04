using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reference");

            migrationBuilder.EnsureSchema(
                name: "returns");

            migrationBuilder.EnsureSchema(
                name: "iam");

            migrationBuilder.CreateTable(
                name: "Institutions",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LicenceCategory = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Institutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReturnTypes",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DueDaysAfterPeriodEnd = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    InstitutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Roles = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Wso2UserId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsDemoAccount = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalSchema: "reference",
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Obligations",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstitutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FirstSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    PeriodFrequency = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PeriodNumber = table.Column<int>(type: "int", nullable: false),
                    PeriodYear = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Obligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Obligations_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalSchema: "reference",
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Obligations_ReturnTypes_ReturnTypeId",
                        column: x => x.ReturnTypeId,
                        principalSchema: "reference",
                        principalTable: "ReturnTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateVersions",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateVersions_ReturnTypes_ReturnTypeId",
                        column: x => x.ReturnTypeId,
                        principalSchema: "reference",
                        principalTable: "ReturnTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ObligationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstitutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastEditedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastEditedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FirstSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsLate = table.Column<bool>(type: "bit", nullable: false),
                    EditVersion = table.Column<int>(type: "int", nullable: false),
                    ValidatedEditVersion = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Submissions_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalSchema: "reference",
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Obligations_ObligationId",
                        column: x => x.ObligationId,
                        principalSchema: "returns",
                        principalTable: "Obligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_ReturnTypes_ReturnTypeId",
                        column: x => x.ReturnTypeId,
                        principalSchema: "reference",
                        principalTable: "ReturnTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_TemplateVersions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalSchema: "reference",
                        principalTable: "TemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Users_LastEditedByUserId",
                        column: x => x.LastEditedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Users_PreparedByUserId",
                        column: x => x.PreparedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Submissions_Users_SubmittedByUserId",
                        column: x => x.SubmittedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateFields",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Section = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Precision = table.Column<int>(type: "int", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateFields_TemplateVersions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalSchema: "reference",
                        principalTable: "TemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ValidationRules",
                schema: "reference",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RuleType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TargetFieldCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MinValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    LeftExpression = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Operator = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RightExpression = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Tolerance = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    ThresholdPercent = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true),
                    VarianceBasis = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationRules_TemplateVersions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalSchema: "reference",
                        principalTable: "TemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionValues",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    NumericValue = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmissionValues_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "returns",
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowEvents",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorDisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowEvents_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "returns",
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValidationFindings",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FieldCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Justification = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    JustifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JustifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationFindings_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "returns",
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ValidationFindings_Users_JustifiedByUserId",
                        column: x => x.JustifiedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ValidationFindings_ValidationRules_RuleId",
                        column: x => x.RuleId,
                        principalSchema: "reference",
                        principalTable: "ValidationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_Code",
                schema: "reference",
                table: "Institutions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Obligations_InstitutionId",
                schema: "returns",
                table: "Obligations",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Obligations_ReturnTypeId",
                schema: "returns",
                table: "Obligations",
                column: "ReturnTypeId");

            // Complex-type columns cannot be indexed through the EF model, so this index is created here.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX [UX_Obligations_Institution_Return_Period] ON [returns].[Obligations] " +
                "([InstitutionId], [ReturnTypeId], [PeriodFrequency], [PeriodYear], [PeriodNumber]);");

            migrationBuilder.CreateIndex(
                name: "IX_Obligations_Status_DueDate",
                schema: "returns",
                table: "Obligations",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnTypes_Code",
                schema: "reference",
                table: "ReturnTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_DecidedByUserId",
                schema: "returns",
                table: "Submissions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_InstitutionId_Status",
                schema: "returns",
                table: "Submissions",
                columns: new[] { "InstitutionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_LastEditedByUserId",
                schema: "returns",
                table: "Submissions",
                column: "LastEditedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ObligationId",
                schema: "returns",
                table: "Submissions",
                column: "ObligationId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_PreparedByUserId",
                schema: "returns",
                table: "Submissions",
                column: "PreparedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ReturnTypeId",
                schema: "returns",
                table: "Submissions",
                column: "ReturnTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ReviewedByUserId",
                schema: "returns",
                table: "Submissions",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_Status_LastSubmittedAt",
                schema: "returns",
                table: "Submissions",
                columns: new[] { "Status", "LastSubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_SubmittedByUserId",
                schema: "returns",
                table: "Submissions",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_TemplateVersionId",
                schema: "returns",
                table: "Submissions",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionValues_SubmissionId_FieldCode",
                schema: "returns",
                table: "SubmissionValues",
                columns: new[] { "SubmissionId", "FieldCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateFields_TemplateVersionId_Code",
                schema: "reference",
                table: "TemplateFields",
                columns: new[] { "TemplateVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateVersions_ReturnTypeId_Version",
                schema: "reference",
                table: "TemplateVersions",
                columns: new[] { "ReturnTypeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_InstitutionId",
                schema: "iam",
                table: "Users",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                schema: "iam",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Wso2UserId",
                schema: "iam",
                table: "Users",
                column: "Wso2UserId",
                unique: true,
                filter: "[Wso2UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFindings_JustifiedByUserId",
                schema: "returns",
                table: "ValidationFindings",
                column: "JustifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFindings_RuleId",
                schema: "returns",
                table: "ValidationFindings",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationFindings_SubmissionId_Revision",
                schema: "returns",
                table: "ValidationFindings",
                columns: new[] { "SubmissionId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRules_TemplateVersionId_Code",
                schema: "reference",
                table: "ValidationRules",
                columns: new[] { "TemplateVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEvents_ActorUserId",
                schema: "returns",
                table: "WorkflowEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowEvents_SubmissionId_OccurredAt",
                schema: "returns",
                table: "WorkflowEvents",
                columns: new[] { "SubmissionId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [UX_Obligations_Institution_Return_Period] ON [returns].[Obligations];");

            migrationBuilder.DropTable(
                name: "SubmissionValues",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "TemplateFields",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "ValidationFindings",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "WorkflowEvents",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "ValidationRules",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "Submissions",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "Obligations",
                schema: "returns");

            migrationBuilder.DropTable(
                name: "TemplateVersions",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "ReturnTypes",
                schema: "reference");

            migrationBuilder.DropTable(
                name: "Institutions",
                schema: "reference");
        }
    }
}

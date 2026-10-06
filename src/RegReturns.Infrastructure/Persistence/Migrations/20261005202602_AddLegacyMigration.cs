using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "migration");

            migrationBuilder.CreateTable(
                name: "Runs",
                schema: "migration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsDryRun = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    MappingSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RowsRead = table.Column<int>(type: "int", nullable: false),
                    BlankRows = table.Column<int>(type: "int", nullable: false),
                    SupersededRows = table.Column<int>(type: "int", nullable: false),
                    RejectedRows = table.Column<int>(type: "int", nullable: false),
                    MigratedReturns = table.Column<int>(type: "int", nullable: false),
                    AlreadyMigratedReturns = table.Column<int>(type: "int", nullable: false),
                    Mismatches = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RowErrors",
                schema: "migration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MigrationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RowErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RowErrors_Runs_MigrationRunId",
                        column: x => x.MigrationRunId,
                        principalSchema: "migration",
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RunFiles",
                schema: "migration",
                columns: table => new
                {
                    MigrationRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ReturnTypeCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunFiles", x => new { x.MigrationRunId, x.Id });
                    table.ForeignKey(
                        name: "FK_RunFiles_Runs_MigrationRunId",
                        column: x => x.MigrationRunId,
                        principalSchema: "migration",
                        principalTable: "Runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RowErrors_MigrationRunId_FileName_LineNumber",
                schema: "migration",
                table: "RowErrors",
                columns: new[] { "MigrationRunId", "FileName", "LineNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_RunFiles_MigrationRunId_FileName",
                schema: "migration",
                table: "RunFiles",
                columns: new[] { "MigrationRunId", "FileName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_StartedAt",
                schema: "migration",
                table: "Runs",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RowErrors",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "RunFiles",
                schema: "migration");

            migrationBuilder.DropTable(
                name: "Runs",
                schema: "migration");

            migrationBuilder.DropSchema(
                name: "migration");
        }
    }
}

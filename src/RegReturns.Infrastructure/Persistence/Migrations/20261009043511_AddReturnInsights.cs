using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReturnInsights",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    FallbackReason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    AuditSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReturnInsights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReturnInsights_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "returns",
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReturnInsights_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInsights_RequestedByUserId",
                schema: "returns",
                table: "ReturnInsights",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReturnInsights_SubmissionId_Revision_CreatedAt",
                schema: "returns",
                table: "ReturnInsights",
                columns: new[] { "SubmissionId", "Revision", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReturnInsights",
                schema: "returns");
        }
    }
}

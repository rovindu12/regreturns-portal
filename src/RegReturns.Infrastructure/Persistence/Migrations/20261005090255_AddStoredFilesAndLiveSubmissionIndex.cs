using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredFilesAndLiveSubmissionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Submissions_ObligationId",
                schema: "returns",
                table: "Submissions");

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "returns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", maxLength: 5242880, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoredFiles_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalSchema: "returns",
                        principalTable: "Submissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoredFiles_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "iam",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Submissions_LiveObligation",
                schema: "returns",
                table: "Submissions",
                column: "ObligationId",
                unique: true,
                filter: "[Status] <> N'Rejected'");

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_SubmissionId_UploadedAt",
                schema: "returns",
                table: "StoredFiles",
                columns: new[] { "SubmissionId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_UploadedByUserId",
                schema: "returns",
                table: "StoredFiles",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredFiles",
                schema: "returns");

            migrationBuilder.DropIndex(
                name: "UX_Submissions_LiveObligation",
                schema: "returns",
                table: "Submissions");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ObligationId",
                schema: "returns",
                table: "Submissions",
                column: "ObligationId");
        }
    }
}

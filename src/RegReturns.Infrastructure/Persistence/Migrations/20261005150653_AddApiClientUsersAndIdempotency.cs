using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiClientUsersAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "api");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "iam",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<Guid>(
                name: "ApiClientId",
                schema: "iam",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "api",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Key = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    Fingerprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ResponseBody = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_ApiClientId",
                schema: "iam",
                table: "Users",
                column: "ApiClientId",
                unique: true,
                filter: "[ApiClientId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ClientId_Key",
                schema: "api",
                table: "IdempotencyRecords",
                columns: new[] { "ClientId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ExpiresAt",
                schema: "api",
                table: "IdempotencyRecords",
                column: "ExpiresAt");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_ApiClients_ApiClientId",
                schema: "iam",
                table: "Users",
                column: "ApiClientId",
                principalSchema: "iam",
                principalTable: "ApiClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_ApiClients_ApiClientId",
                schema: "iam",
                table: "Users");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "api");

            migrationBuilder.DropIndex(
                name: "IX_Users_ApiClientId",
                schema: "iam",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApiClientId",
                schema: "iam",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                schema: "iam",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}

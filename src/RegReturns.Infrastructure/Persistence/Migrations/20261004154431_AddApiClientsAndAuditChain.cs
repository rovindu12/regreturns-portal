using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApiClientsAndAuditChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "ApiClients",
                schema: "iam",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InstitutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Wso2ClientId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiClients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiClients_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalSchema: "reference",
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "audit",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActorSubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorDisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ActorType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InstitutionCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    PreviousHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Hash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_InstitutionId",
                schema: "iam",
                table: "ApiClients",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiClients_Wso2ClientId",
                schema: "iam",
                table: "ApiClients",
                column: "Wso2ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ActorSubjectId_OccurredAt",
                schema: "audit",
                table: "AuditEntries",
                columns: new[] { "ActorSubjectId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                schema: "audit",
                table: "AuditEntries",
                column: "OccurredAt");

            // Append-only at the database level too: updates and deletes fail unless someone deliberately
            // disables the trigger, and the HMAC chain exposes any change made that way (ADR 0016).
            migrationBuilder.Sql("""
                CREATE TRIGGER [audit].[TR_AuditEntries_AppendOnly] ON [audit].[AuditEntries]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51001, N'Audit entries are append-only.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [audit].[TR_AuditEntries_AppendOnly];");

            migrationBuilder.DropTable(
                name: "ApiClients",
                schema: "iam");

            migrationBuilder.DropTable(
                name: "AuditEntries",
                schema: "audit");
        }
    }
}

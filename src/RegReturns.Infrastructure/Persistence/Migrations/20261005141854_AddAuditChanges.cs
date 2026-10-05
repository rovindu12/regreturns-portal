using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Changes",
                schema: "audit",
                table: "AuditEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                schema: "audit",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                schema: "audit",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "Changes",
                schema: "audit",
                table: "AuditEntries");
        }
    }
}

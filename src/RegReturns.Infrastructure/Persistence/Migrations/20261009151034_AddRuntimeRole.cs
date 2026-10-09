using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RegReturns.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the <c>regreturns_runtime</c> role (ADR 0034): data access for the running apps and nothing else. Granting at
    /// database scope covers tables a later migration adds; the denial keeps the audit chain append-only even if the
    /// trigger were gone. The role holds no members here: the deployment adds its application login.
    /// </summary>
    public partial class AddRuntimeRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                IF DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Runtime}') IS NULL
                    CREATE ROLE [{DatabaseRoles.Runtime}];
                """);
            migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE TO [{DatabaseRoles.Runtime}];");
            migrationBuilder.Sql($"DENY UPDATE, DELETE ON OBJECT::[audit].[AuditEntries] TO [{DatabaseRoles.Runtime}];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A role with members cannot be dropped: remove them first.
            migrationBuilder.Sql($"""
                IF DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Runtime}') IS NOT NULL
                BEGIN
                    DECLARE @members nvarchar(max) = N'';
                    SELECT @members += N'ALTER ROLE [{DatabaseRoles.Runtime}] DROP MEMBER ' + QUOTENAME(USER_NAME(member_principal_id)) + N';'
                    FROM sys.database_role_members
                    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'{DatabaseRoles.Runtime}');
                    EXEC sp_executesql @members;
                    DROP ROLE [{DatabaseRoles.Runtime}];
                END
                """);
        }
    }
}

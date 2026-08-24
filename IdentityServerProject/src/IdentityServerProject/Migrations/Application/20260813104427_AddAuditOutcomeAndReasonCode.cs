using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityServerProject.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditOutcomeAndReasonCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "AuditLogEntries",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "AuditLogEntries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE AuditLogEntries
                SET Outcome = CASE WHEN IsSuccess = 1 THEN 'Succeeded' ELSE 'Failed' END,
                    ReasonCode = CASE WHEN IsSuccess = 1 THEN 'Succeeded' ELSE 'LegacyUnspecified' END
                WHERE Outcome IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Outcome",
                table: "AuditLogEntries",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReasonCode",
                table: "AuditLogEntries",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_Timestamp",
                table: "AuditLogEntries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogEntries_Timestamp",
                table: "AuditLogEntries");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "AuditLogEntries");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "AuditLogEntries");
        }
    }
}

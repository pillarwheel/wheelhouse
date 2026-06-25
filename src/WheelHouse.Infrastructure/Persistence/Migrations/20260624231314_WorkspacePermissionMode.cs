using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WheelHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkspacePermissionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PermissionMode",
                table: "Workspaces",
                type: "TEXT",
                nullable: false,
                defaultValue: "acceptEdits");

            // Backfill existing rows so pre-existing workspaces get the safe default.
            migrationBuilder.Sql(
                "UPDATE Workspaces SET PermissionMode = 'acceptEdits' " +
                "WHERE PermissionMode IS NULL OR PermissionMode = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PermissionMode",
                table: "Workspaces");
        }
    }
}

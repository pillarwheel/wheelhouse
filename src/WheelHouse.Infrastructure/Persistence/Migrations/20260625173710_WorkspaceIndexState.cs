using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WheelHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceIndexState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IndexStatus",
                table: "Workspaces",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "IndexedFileCount",
                table: "Workspaces",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIndexedAt",
                table: "Workspaces",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexStatus",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "IndexedFileCount",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "LastIndexedAt",
                table: "Workspaces");
        }
    }
}

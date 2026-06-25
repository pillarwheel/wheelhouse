using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WheelHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionTranscript : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    ToolName = table.Column<string>(type: "TEXT", nullable: true),
                    IsError = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionEvents_Sessions_AgentSessionId",
                        column: x => x.AgentSessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionEvents_AgentSessionId_Id",
                table: "SessionEvents",
                columns: new[] { "AgentSessionId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionEvents");
        }
    }
}

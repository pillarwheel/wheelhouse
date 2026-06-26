using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WheelHouse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TemplateId",
                table: "Sessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SessionTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    StepsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TemplateId",
                table: "Sessions",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_SessionTemplates_TemplateId",
                table: "Sessions",
                column: "TemplateId",
                principalTable: "SessionTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_SessionTemplates_TemplateId",
                table: "Sessions");

            migrationBuilder.DropTable(
                name: "SessionTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_TemplateId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Sessions");
        }
    }
}

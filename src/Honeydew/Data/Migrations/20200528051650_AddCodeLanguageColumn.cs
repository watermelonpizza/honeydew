using Microsoft.EntityFrameworkCore.Migrations;

namespace Honeydew.Data.Migrations
{
    public partial class AddCodeLanguageColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CodeLanguage",
                table: "Uploads",
                maxLength: 25,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodeLanguage",
                table: "Uploads");
        }
    }
}

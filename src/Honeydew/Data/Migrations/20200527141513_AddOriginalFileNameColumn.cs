using Microsoft.EntityFrameworkCore.Migrations;

namespace Honeydew.Data.Migrations
{
    public partial class AddOriginalFileNameColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalFileNameWithExtension",
                table: "Uploads",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OriginalFileNameWithExtension",
                table: "Uploads");
        }
    }
}

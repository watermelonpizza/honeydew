using Microsoft.EntityFrameworkCore.Migrations;

namespace Honeydew.Data.Migrations
{
    public partial class RenameContentTypeToMediaType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ContentType",
                newName: "MediaType",
                table: "Uploads");

            migrationBuilder.AddColumn<string>(
                name: "Url",
                table: "Uploads",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MediaType",
                newName: "ContentType",
                table: "Uploads");

            migrationBuilder.DropColumn(
                name: "Url",
                table: "Uploads");
        }
    }
}

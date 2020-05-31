using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Honeydew.Data.Migrations
{
    public partial class AddPendingDeletionAtColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingForDeletionAt",
                table: "Uploads",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingForDeletionAt",
                table: "Uploads");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace ServeX.Migrations
{
    public partial class ArchiveMenuItems : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>("Revision", "Orders", type: "INTEGER",
                nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>("IsArchived", "MenuItems", type: "INTEGER",
                nullable: false, defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("Revision", "Orders");
            migrationBuilder.DropColumn("IsArchived", "MenuItems");
        }
    }
}

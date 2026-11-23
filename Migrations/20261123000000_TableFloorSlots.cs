using Microsoft.EntityFrameworkCore.Migrations;

namespace ServeX.Migrations
{
    public partial class TableFloorSlots : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>("FloorSlot", "Tables", type: "INTEGER", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("FloorSlot", "Tables");
        }
    }
}

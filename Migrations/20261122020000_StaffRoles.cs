using Microsoft.EntityFrameworkCore.Migrations;

namespace ServeX.Migrations
{
    public partial class StaffRoles : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>("KitchenSentQuantity", "OrderItems", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>("PaymentReference", "Bills", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<System.DateTime>("CancelledAt", "Orders", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<int>("Role", "Users", type: "INTEGER", nullable: false, defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("KitchenSentQuantity", "OrderItems");
            migrationBuilder.DropColumn("PaymentReference", "Bills");
            migrationBuilder.DropColumn("CancelledAt", "Orders");
            migrationBuilder.DropColumn("Role", "Users");
        }
    }
}

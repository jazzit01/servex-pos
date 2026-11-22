using Microsoft.EntityFrameworkCore.Migrations;

namespace ServeX.Migrations
{
    public partial class ReceiptPreferences : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>("PaymentBreakdown", "Bills", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>("DiscountReason", "Bills", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>("Locale", "Settings", type: "TEXT", nullable: false, defaultValue: "en-IN");
            migrationBuilder.AddColumn<string>("TaxName", "Settings", type: "TEXT", nullable: false, defaultValue: "Tax");
            migrationBuilder.AddColumn<string>("TaxNumber", "Settings", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<int>("ReceiptWidth", "Settings", type: "INTEGER", nullable: false, defaultValue: 80);
            migrationBuilder.AddColumn<bool>("TaxExempt", "MenuItems", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>("TaxExempt", "OrderItems", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<decimal>("RecordedTaxRate", "Bills", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>("RecordedTaxName", "Bills", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>("RecordedTaxNumber", "Bills", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>("RecordedLocale", "Bills", type: "TEXT", nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn("PaymentBreakdown", "Bills");
            migrationBuilder.DropColumn("DiscountReason", "Bills");
            migrationBuilder.DropColumn("Locale", "Settings");
            migrationBuilder.DropColumn("TaxName", "Settings");
            migrationBuilder.DropColumn("TaxNumber", "Settings");
            migrationBuilder.DropColumn("ReceiptWidth", "Settings");
            migrationBuilder.DropColumn("TaxExempt", "MenuItems");
            migrationBuilder.DropColumn("TaxExempt", "OrderItems");
            migrationBuilder.DropColumn("RecordedTaxRate", "Bills");
            migrationBuilder.DropColumn("RecordedTaxName", "Bills");
            migrationBuilder.DropColumn("RecordedTaxNumber", "Bills");
            migrationBuilder.DropColumn("RecordedLocale", "Bills");
        }
    }
}

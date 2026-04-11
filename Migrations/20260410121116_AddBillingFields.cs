using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServeX.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Bills",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Bills",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Bills");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Bills");
        }
    }
}

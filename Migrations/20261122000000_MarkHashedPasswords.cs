using Microsoft.EntityFrameworkCore.Migrations;

namespace ServeX.Migrations
{
    public partial class MarkHashedPasswords : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPasswordHashed",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Password hashes cannot be converted back to plaintext.");
        }
    }
}

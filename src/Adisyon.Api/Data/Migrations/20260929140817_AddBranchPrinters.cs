using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adisyon.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "kitchen_printer_address",
                table: "branches",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "printer_code_page",
                table: "branches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 13);

            migrationBuilder.AddColumn<string>(
                name: "receipt_printer_address",
                table: "branches",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kitchen_printer_address",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "printer_code_page",
                table: "branches");

            migrationBuilder.DropColumn(
                name: "receipt_printer_address",
                table: "branches");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adisyon.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "position",
                table: "order_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "position",
                table: "order_items");
        }
    }
}

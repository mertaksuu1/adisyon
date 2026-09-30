using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adisyon.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdjustedAtTableName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "adjusted_at_table_name",
                table: "order_items",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "adjusted_at_table_name",
                table: "order_items");
        }
    }
}

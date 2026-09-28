using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adisyon.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "table_sessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_created_by_user_id",
                table: "orders",
                column: "created_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_users_created_by_user_id",
                table: "orders",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_orders_users_created_by_user_id",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_created_by_user_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "table_sessions");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "orders");
        }
    }
}

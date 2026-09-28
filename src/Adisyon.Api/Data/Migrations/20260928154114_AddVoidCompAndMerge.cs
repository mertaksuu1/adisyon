using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adisyon.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVoidCompAndMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_session_id",
                table: "table_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "comped_at",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "comped_by_user_id",
                table: "order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                table: "order_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "voided_by_user_id",
                table: "order_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_table_sessions_merged_into_session_id",
                table: "table_sessions",
                column: "merged_into_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_comped_by_user_id",
                table: "order_items",
                column: "comped_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_voided_by_user_id",
                table: "order_items",
                column: "voided_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_users_comped_by_user_id",
                table: "order_items",
                column: "comped_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_order_items_users_voided_by_user_id",
                table: "order_items",
                column: "voided_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_table_sessions_table_sessions_merged_into_session_id",
                table: "table_sessions",
                column: "merged_into_session_id",
                principalTable: "table_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_order_items_users_comped_by_user_id",
                table: "order_items");

            migrationBuilder.DropForeignKey(
                name: "fk_order_items_users_voided_by_user_id",
                table: "order_items");

            migrationBuilder.DropForeignKey(
                name: "fk_table_sessions_table_sessions_merged_into_session_id",
                table: "table_sessions");

            migrationBuilder.DropIndex(
                name: "ix_table_sessions_merged_into_session_id",
                table: "table_sessions");

            migrationBuilder.DropIndex(
                name: "ix_order_items_comped_by_user_id",
                table: "order_items");

            migrationBuilder.DropIndex(
                name: "ix_order_items_voided_by_user_id",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "merged_into_session_id",
                table: "table_sessions");

            migrationBuilder.DropColumn(
                name: "comped_at",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "comped_by_user_id",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "voided_at",
                table: "order_items");

            migrationBuilder.DropColumn(
                name: "voided_by_user_id",
                table: "order_items");
        }
    }
}

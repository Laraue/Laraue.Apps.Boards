using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroActionAssignee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assignee_id",
                table: "retro_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_retro_cards_assignee_id",
                table: "retro_cards",
                column: "assignee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards",
                column: "assignee_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards");

            migrationBuilder.DropIndex(
                name: "ix_retro_cards_assignee_id",
                table: "retro_cards");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "retro_cards");
        }
    }
}

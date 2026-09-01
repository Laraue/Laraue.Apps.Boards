using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class ClearRetroActionAssigneeOnUserDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards");

            migrationBuilder.AddForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards",
                column: "assignee_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards");

            migrationBuilder.AddForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards",
                column: "assignee_id",
                principalTable: "users",
                principalColumn: "id");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CreateLinkedTelegramChatAndAddLinkChatPermission2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_linked_telegram_chat_statuses_status_id",
                table: "linked_telegram_chat");

            migrationBuilder.DropForeignKey(
                name: "fk_linked_telegram_chat_users_owner_id",
                table: "linked_telegram_chat");

            migrationBuilder.DropPrimaryKey(
                name: "pk_linked_telegram_chat",
                table: "linked_telegram_chat");

            migrationBuilder.RenameTable(
                name: "linked_telegram_chat",
                newName: "linked_telegram_chats");

            migrationBuilder.RenameIndex(
                name: "ix_linked_telegram_chat_status_id",
                table: "linked_telegram_chats",
                newName: "ix_linked_telegram_chats_status_id");

            migrationBuilder.RenameIndex(
                name: "ix_linked_telegram_chat_owner_id",
                table: "linked_telegram_chats",
                newName: "ix_linked_telegram_chats_owner_id");

            migrationBuilder.RenameIndex(
                name: "ix_linked_telegram_chat_external_chat_id",
                table: "linked_telegram_chats",
                newName: "ix_linked_telegram_chats_external_chat_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_linked_telegram_chats",
                table: "linked_telegram_chats",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_linked_telegram_chats_statuses_status_id",
                table: "linked_telegram_chats",
                column: "status_id",
                principalTable: "statuses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_linked_telegram_chats_users_owner_id",
                table: "linked_telegram_chats",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_linked_telegram_chats_statuses_status_id",
                table: "linked_telegram_chats");

            migrationBuilder.DropForeignKey(
                name: "fk_linked_telegram_chats_users_owner_id",
                table: "linked_telegram_chats");

            migrationBuilder.DropPrimaryKey(
                name: "pk_linked_telegram_chats",
                table: "linked_telegram_chats");

            migrationBuilder.RenameTable(
                name: "linked_telegram_chats",
                newName: "linked_telegram_chat");

            migrationBuilder.RenameIndex(
                name: "ix_linked_telegram_chats_status_id",
                table: "linked_telegram_chat",
                newName: "ix_linked_telegram_chat_status_id");

            migrationBuilder.RenameIndex(
                name: "ix_linked_telegram_chats_owner_id",
                table: "linked_telegram_chat",
                newName: "ix_linked_telegram_chat_owner_id");

            migrationBuilder.RenameIndex(
                name: "ix_linked_telegram_chats_external_chat_id",
                table: "linked_telegram_chat",
                newName: "ix_linked_telegram_chat_external_chat_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_linked_telegram_chat",
                table: "linked_telegram_chat",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_linked_telegram_chat_statuses_status_id",
                table: "linked_telegram_chat",
                column: "status_id",
                principalTable: "statuses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_linked_telegram_chat_users_owner_id",
                table: "linked_telegram_chat",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id");
        }
    }
}

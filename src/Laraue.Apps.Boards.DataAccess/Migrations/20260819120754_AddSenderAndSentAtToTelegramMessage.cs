using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddSenderAndSentAtToTelegramMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sender_id",
                table: "telegram_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "sent_at",
                table: "telegram_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_sender_id",
                table: "telegram_messages",
                column: "sender_id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_users_sender_id",
                table: "telegram_messages",
                column: "sender_id",
                principalTable: "users",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_users_sender_id",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_sender_id",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "sender_id",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "sent_at",
                table: "telegram_messages");
        }
    }
}

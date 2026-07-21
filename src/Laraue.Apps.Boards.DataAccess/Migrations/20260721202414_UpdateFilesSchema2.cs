using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class UpdateFilesSchema2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id1",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_attachment_id1",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "attachment_id1",
                table: "telegram_messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "attachment_id",
                table: "telegram_messages",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_attachment_id",
                table: "telegram_messages",
                column: "attachment_id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id",
                table: "telegram_messages",
                column: "attachment_id",
                principalTable: "attachments",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_attachment_id",
                table: "telegram_messages");

            migrationBuilder.AlterColumn<long>(
                name: "attachment_id",
                table: "telegram_messages",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "attachment_id1",
                table: "telegram_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_attachment_id1",
                table: "telegram_messages",
                column: "attachment_id1");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id1",
                table: "telegram_messages",
                column: "attachment_id1",
                principalTable: "attachments",
                principalColumn: "id");
        }
    }
}

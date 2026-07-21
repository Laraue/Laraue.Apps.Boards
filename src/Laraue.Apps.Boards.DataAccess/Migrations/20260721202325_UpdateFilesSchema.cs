using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class UpdateFilesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_telegram_files_telegram_file_id",
                table: "telegram_messages");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_telegram_files_telegram_preview_file_id",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_telegram_file_id",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_telegram_preview_file_id",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_issue_attachments_attachment_id",
                table: "issue_attachments");

            migrationBuilder.DropColumn(
                name: "attachment_type",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "telegram_file_id",
                table: "telegram_messages");

            migrationBuilder.RenameColumn(
                name: "telegram_preview_file_id",
                table: "telegram_messages",
                newName: "attachment_id");

            migrationBuilder.AddColumn<Guid>(
                name: "attachment_id1",
                table: "telegram_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_attachment_id1",
                table: "telegram_messages",
                column: "attachment_id1");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attachments_attachment_id",
                table: "issue_attachments",
                column: "attachment_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id1",
                table: "telegram_messages",
                column: "attachment_id1",
                principalTable: "attachments",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id1",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_attachment_id1",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_issue_attachments_attachment_id",
                table: "issue_attachments");

            migrationBuilder.DropColumn(
                name: "attachment_id1",
                table: "telegram_messages");

            migrationBuilder.RenameColumn(
                name: "attachment_id",
                table: "telegram_messages",
                newName: "telegram_preview_file_id");

            migrationBuilder.AddColumn<int>(
                name: "attachment_type",
                table: "telegram_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "telegram_file_id",
                table: "telegram_messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_telegram_file_id",
                table: "telegram_messages",
                column: "telegram_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_telegram_preview_file_id",
                table: "telegram_messages",
                column: "telegram_preview_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attachments_attachment_id",
                table: "issue_attachments",
                column: "attachment_id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_telegram_files_telegram_file_id",
                table: "telegram_messages",
                column: "telegram_file_id",
                principalTable: "telegram_files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_telegram_files_telegram_preview_file_id",
                table: "telegram_messages",
                column: "telegram_preview_file_id",
                principalTable: "telegram_files",
                principalColumn: "id");
        }
    }
}

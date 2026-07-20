using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RenameTelegramPhotoAndVideoTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_photos_telegram_files_telegram_file_id",
                table: "telegram_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_photos_telegram_messages_telegram_message_id",
                table: "telegram_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_videos_telegram_files_file_id",
                table: "telegram_videos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_videos_telegram_files_thumbnail_file_id",
                table: "telegram_videos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_videos_telegram_messages_telegram_message_id",
                table: "telegram_videos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_telegram_videos",
                table: "telegram_videos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_telegram_photos",
                table: "telegram_photos");

            migrationBuilder.RenameTable(
                name: "telegram_videos",
                newName: "telegram_message_videos");

            migrationBuilder.RenameTable(
                name: "telegram_photos",
                newName: "telegram_message_photos");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_videos_thumbnail_file_id",
                table: "telegram_message_videos",
                newName: "ix_telegram_message_videos_thumbnail_file_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_videos_telegram_message_id",
                table: "telegram_message_videos",
                newName: "ix_telegram_message_videos_telegram_message_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_videos_file_id",
                table: "telegram_message_videos",
                newName: "ix_telegram_message_videos_file_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_photos_telegram_message_id",
                table: "telegram_message_photos",
                newName: "ix_telegram_message_photos_telegram_message_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_photos_telegram_file_id",
                table: "telegram_message_photos",
                newName: "ix_telegram_message_photos_telegram_file_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_telegram_message_videos",
                table: "telegram_message_videos",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_telegram_message_photos",
                table: "telegram_message_photos",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_message_photos_telegram_files_telegram_file_id",
                table: "telegram_message_photos",
                column: "telegram_file_id",
                principalTable: "telegram_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_message_photos_telegram_messages_telegram_message_",
                table: "telegram_message_photos",
                column: "telegram_message_id",
                principalTable: "telegram_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_message_videos_telegram_files_file_id",
                table: "telegram_message_videos",
                column: "file_id",
                principalTable: "telegram_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_message_videos_telegram_files_thumbnail_file_id",
                table: "telegram_message_videos",
                column: "thumbnail_file_id",
                principalTable: "telegram_files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_message_videos_telegram_messages_telegram_message_",
                table: "telegram_message_videos",
                column: "telegram_message_id",
                principalTable: "telegram_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_message_photos_telegram_files_telegram_file_id",
                table: "telegram_message_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_message_photos_telegram_messages_telegram_message_",
                table: "telegram_message_photos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_message_videos_telegram_files_file_id",
                table: "telegram_message_videos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_message_videos_telegram_files_thumbnail_file_id",
                table: "telegram_message_videos");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_message_videos_telegram_messages_telegram_message_",
                table: "telegram_message_videos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_telegram_message_videos",
                table: "telegram_message_videos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_telegram_message_photos",
                table: "telegram_message_photos");

            migrationBuilder.RenameTable(
                name: "telegram_message_videos",
                newName: "telegram_videos");

            migrationBuilder.RenameTable(
                name: "telegram_message_photos",
                newName: "telegram_photos");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_message_videos_thumbnail_file_id",
                table: "telegram_videos",
                newName: "ix_telegram_videos_thumbnail_file_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_message_videos_telegram_message_id",
                table: "telegram_videos",
                newName: "ix_telegram_videos_telegram_message_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_message_videos_file_id",
                table: "telegram_videos",
                newName: "ix_telegram_videos_file_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_message_photos_telegram_message_id",
                table: "telegram_photos",
                newName: "ix_telegram_photos_telegram_message_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_message_photos_telegram_file_id",
                table: "telegram_photos",
                newName: "ix_telegram_photos_telegram_file_id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_telegram_videos",
                table: "telegram_videos",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_telegram_photos",
                table: "telegram_photos",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_photos_telegram_files_telegram_file_id",
                table: "telegram_photos",
                column: "telegram_file_id",
                principalTable: "telegram_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_photos_telegram_messages_telegram_message_id",
                table: "telegram_photos",
                column: "telegram_message_id",
                principalTable: "telegram_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_videos_telegram_files_file_id",
                table: "telegram_videos",
                column: "file_id",
                principalTable: "telegram_files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_videos_telegram_files_thumbnail_file_id",
                table: "telegram_videos",
                column: "thumbnail_file_id",
                principalTable: "telegram_files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_videos_telegram_messages_telegram_message_id",
                table: "telegram_videos",
                column: "telegram_message_id",
                principalTable: "telegram_messages",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

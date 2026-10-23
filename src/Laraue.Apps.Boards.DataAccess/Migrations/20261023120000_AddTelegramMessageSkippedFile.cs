using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramMessageSkippedFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "skipped_file_name",
                table: "telegram_messages",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "skipped_file_reason",
                table: "telegram_messages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "skipped_file_unique_id",
                table: "telegram_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_skipped_file_unique_id",
                table: "telegram_messages",
                column: "skipped_file_unique_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_skipped_file_unique_id",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "skipped_file_unique_id",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "skipped_file_name",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "skipped_file_reason",
                table: "telegram_messages");
        }
    }
}

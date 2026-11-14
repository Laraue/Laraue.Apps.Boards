using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedTelegramChatReplyMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "reply_mode",
                table: "linked_telegram_chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Keep what every existing chat does: auto-save reacts (Reaction = 0, the default above), /save replies
            // with the card link (SaveMode.BotMentionedMessages = 1 -> ReplyMode.IssueLink = 1).
            migrationBuilder.Sql("UPDATE linked_telegram_chats SET reply_mode = 1 WHERE save_mode = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reply_mode",
                table: "linked_telegram_chats");
        }
    }
}

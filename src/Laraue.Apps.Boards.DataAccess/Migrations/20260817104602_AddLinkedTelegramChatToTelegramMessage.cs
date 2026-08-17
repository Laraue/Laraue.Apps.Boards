using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedTelegramChatToTelegramMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "linked_telegram_chat_id",
                table: "telegram_messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_messages_linked_telegram_chat_id",
                table: "telegram_messages",
                column: "linked_telegram_chat_id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_linked_telegram_chats_linked_telegram_cha",
                table: "telegram_messages",
                column: "linked_telegram_chat_id",
                principalTable: "linked_telegram_chats",
                principalColumn: "id");

            // Backfill: every pre-existing user gets an explicit LinkedTelegramChat for their
            // private chat, reproducing the old implicit "save to personal org" behaviour.
            migrationBuilder.Sql(@"
insert into linked_telegram_chats (external_chat_id, title, status_id, owner_id, save_mode, linked_at)
select distinct on (u.id) u.telegram_id, coalesce(u.telegram_user_name, u.telegram_first_name), s.id, u.id, 0, now()
from users u
join organizations o on o.owner_id = u.id and o.type = 1 /** Personal **/
join spaces sp on sp.organization_id = o.id and sp.is_default = true
join epics e on e.space_id = sp.id and e.is_default = true
join statuses s on s.epic_id = e.id
where not exists (select 1 from linked_telegram_chats lc where lc.external_chat_id = u.telegram_id)
order by u.id, s.sort_order;

update telegram_messages tm
set linked_telegram_chat_id = lc.id
from linked_telegram_chats lc
where tm.linked_telegram_chat_id is null
  and tm.external_chat_id = lc.external_chat_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_linked_telegram_chats_linked_telegram_cha",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_linked_telegram_chat_id",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "linked_telegram_chat_id",
                table: "telegram_messages");
        }
    }
}

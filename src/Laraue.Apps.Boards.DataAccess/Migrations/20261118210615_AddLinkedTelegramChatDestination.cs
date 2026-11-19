using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <summary>
    /// BRD-365: a linked chat's destination becomes its organization plus how far down it is fixed
    /// (<c>destination_type</c> + <c>destination_id</c>) instead of a status. Every existing chat is fixed down to the
    /// status it had, so nothing changes for it. Written by hand: the generated version renamed <c>status_id</c> to
    /// <c>organization_id</c>, which would have put status ids in the organization column.
    /// </summary>
    public partial class AddLinkedTelegramChatDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "organization_id",
                table: "linked_telegram_chats",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "destination_type",
                table: "linked_telegram_chats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "destination_id",
                table: "linked_telegram_chats",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Fixed down to the chat's status (LinkDestinationType.Status = 3), in the organization that status is in.
            migrationBuilder.Sql(
                """
                UPDATE linked_telegram_chats AS c
                SET organization_id = sp.organization_id,
                    destination_type = 3,
                    destination_id = c.status_id
                FROM statuses AS st
                JOIN epics AS e ON e.id = st.epic_id
                JOIN spaces AS sp ON sp.id = e.space_id
                WHERE st.id = c.status_id;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "organization_id",
                table: "linked_telegram_chats",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "fk_linked_telegram_chats_statuses_status_id",
                table: "linked_telegram_chats");

            migrationBuilder.DropIndex(
                name: "ix_linked_telegram_chats_status_id",
                table: "linked_telegram_chats");

            migrationBuilder.DropColumn(
                name: "status_id",
                table: "linked_telegram_chats");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_organization_id",
                table: "linked_telegram_chats",
                column: "organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_linked_telegram_chats_organizations_organization_id",
                table: "linked_telegram_chats",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "status_id",
                table: "linked_telegram_chats",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("UPDATE linked_telegram_chats SET status_id = destination_id WHERE destination_type = 3;");

            // A chat that asks for part of its destination has no single status to go back to.
            migrationBuilder.Sql("DELETE FROM linked_telegram_chats WHERE status_id IS NULL;");

            migrationBuilder.AlterColumn<long>(
                name: "status_id",
                table: "linked_telegram_chats",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "fk_linked_telegram_chats_organizations_organization_id",
                table: "linked_telegram_chats");

            migrationBuilder.DropIndex(
                name: "ix_linked_telegram_chats_organization_id",
                table: "linked_telegram_chats");

            migrationBuilder.DropColumn(
                name: "organization_id",
                table: "linked_telegram_chats");

            migrationBuilder.DropColumn(
                name: "destination_type",
                table: "linked_telegram_chats");

            migrationBuilder.DropColumn(
                name: "destination_id",
                table: "linked_telegram_chats");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_status_id",
                table: "linked_telegram_chats",
                column: "status_id");

            migrationBuilder.AddForeignKey(
                name: "fk_linked_telegram_chats_statuses_status_id",
                table: "linked_telegram_chats",
                column: "status_id",
                principalTable: "statuses",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

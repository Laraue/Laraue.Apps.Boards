using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CreateLinkedTelegramChatAndAddLinkChatPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "linked_telegram_chats",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status_id = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unlinked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    save_mode = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linked_telegram_chats", x => x.id);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chats_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chats_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_external_chat_id",
                table: "linked_telegram_chats",
                column: "external_chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_linked_at",
                table: "linked_telegram_chats",
                column: "linked_at");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_owner_id",
                table: "linked_telegram_chats",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_status_id",
                table: "linked_telegram_chats",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_unlinked_at",
                table: "linked_telegram_chats",
                column: "unlinked_at");
            
            migrationBuilder.Sql(@"
update organization_users ou set admin_access_level = (admin_access_level | /** LinkChats **/ 32)
from organizations o
where o.owner_id = ou.user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linked_telegram_chats");
        }
    }
}

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
                name: "linked_telegram_chat",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status_id = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linked_telegram_chat", x => x.id);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chat_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chat_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chat_external_chat_id",
                table: "linked_telegram_chat",
                column: "external_chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chat_owner_id",
                table: "linked_telegram_chat",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chat_status_id",
                table: "linked_telegram_chat",
                column: "status_id");
            
            migrationBuilder.Sql(@"
update organization_users ou set admin_access_level = (admin_access_level | /** LinkChats **/ 32)
from organizations o
where o.owner_id = ou.user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linked_telegram_chat");
        }
    }
}

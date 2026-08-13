using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkedTelegramChatsAndCanLinkChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_link_chat",
                table: "organization_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "linked_telegram_chats",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    organization_id = table.Column<long>(type: "bigint", nullable: true),
                    space_id = table.Column<long>(type: "bigint", nullable: true),
                    epic_id = table.Column<long>(type: "bigint", nullable: true),
                    status_id = table.Column<long>(type: "bigint", nullable: true),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linked_telegram_chats", x => x.id);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chats_epics_epic_id",
                        column: x => x.epic_id,
                        principalTable: "epics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chats_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chats_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chats_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_linked_telegram_chats_users_linked_by_user_id",
                        column: x => x.linked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_epic_id",
                table: "linked_telegram_chats",
                column: "epic_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_external_chat_id",
                table: "linked_telegram_chats",
                column: "external_chat_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_linked_by_user_id",
                table: "linked_telegram_chats",
                column: "linked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_organization_id",
                table: "linked_telegram_chats",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_space_id",
                table: "linked_telegram_chats",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "ix_linked_telegram_chats_status_id",
                table: "linked_telegram_chats",
                column: "status_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "linked_telegram_chats");

            migrationBuilder.DropColumn(
                name: "can_link_chat",
                table: "organization_users");
        }
    }
}

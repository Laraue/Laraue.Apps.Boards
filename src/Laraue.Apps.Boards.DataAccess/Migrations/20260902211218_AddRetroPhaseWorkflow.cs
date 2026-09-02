using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroPhaseWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "vote_ends_at",
                table: "retros",
                newName: "phase_ends_at");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "retro_cards",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);

            migrationBuilder.AddColumn<Guid>(
                name: "assignee_id",
                table: "retro_cards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "group_id",
                table: "retro_cards",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stack_order",
                table: "retro_cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "retro_card_groups",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    retro_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retro_card_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_retro_card_groups_retros_retro_id",
                        column: x => x.retro_id,
                        principalTable: "retros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_retro_cards_assignee_id",
                table: "retro_cards",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_retro_cards_group_id",
                table: "retro_cards",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_retro_card_groups_retro_id",
                table: "retro_card_groups",
                column: "retro_id");

            migrationBuilder.AddForeignKey(
                name: "fk_retro_cards_retro_card_groups_group_id",
                table: "retro_cards",
                column: "group_id",
                principalTable: "retro_card_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards",
                column: "assignee_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_retro_cards_retro_card_groups_group_id",
                table: "retro_cards");

            migrationBuilder.DropForeignKey(
                name: "fk_retro_cards_users_assignee_id",
                table: "retro_cards");

            migrationBuilder.DropTable(
                name: "retro_card_groups");

            migrationBuilder.DropIndex(
                name: "ix_retro_cards_assignee_id",
                table: "retro_cards");

            migrationBuilder.DropIndex(
                name: "ix_retro_cards_group_id",
                table: "retro_cards");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "retro_cards");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "retro_cards");

            migrationBuilder.DropColumn(
                name: "stack_order",
                table: "retro_cards");

            migrationBuilder.RenameColumn(
                name: "phase_ends_at",
                table: "retros",
                newName: "vote_ends_at");

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "retro_cards",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}

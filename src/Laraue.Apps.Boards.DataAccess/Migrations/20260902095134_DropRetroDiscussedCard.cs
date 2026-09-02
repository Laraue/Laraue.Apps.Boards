using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class DropRetroDiscussedCard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_retros_retro_cards_discussed_card_id",
                table: "retros");

            migrationBuilder.DropIndex(
                name: "ix_retros_discussed_card_id",
                table: "retros");

            migrationBuilder.DropColumn(
                name: "discussed_card_id",
                table: "retros");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "discussed_card_id",
                table: "retros",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_retros_discussed_card_id",
                table: "retros",
                column: "discussed_card_id");

            migrationBuilder.AddForeignKey(
                name: "fk_retros_retro_cards_discussed_card_id",
                table: "retros",
                column: "discussed_card_id",
                principalTable: "retro_cards",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}

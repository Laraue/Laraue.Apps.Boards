using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddRetros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "retros",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<long>(type: "bigint", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    phase = table.Column<int>(type: "integer", nullable: false),
                    votes_per_user = table.Column<int>(type: "integer", nullable: false),
                    vote_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retros", x => x.id);
                    table.ForeignKey(
                        name: "fk_retros_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_retros_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retro_participants",
                columns: table => new
                {
                    retro_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retro_participants", x => new { x.retro_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_retro_participants_retros_retro_id",
                        column: x => x.retro_id,
                        principalTable: "retros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_retro_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retro_sections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    retro_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retro_sections", x => x.id);
                    table.ForeignKey(
                        name: "fk_retro_sections_retros_retro_id",
                        column: x => x.retro_id,
                        principalTable: "retros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retro_cards",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_id = table.Column<long>(type: "bigint", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    x = table.Column<double>(type: "double precision", nullable: false),
                    y = table.Column<double>(type: "double precision", nullable: false),
                    done = table.Column<bool>(type: "boolean", nullable: false),
                    revealed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retro_cards", x => x.id);
                    table.ForeignKey(
                        name: "fk_retro_cards_retro_sections_section_id",
                        column: x => x.section_id,
                        principalTable: "retro_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_retro_cards_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retro_card_votes",
                columns: table => new
                {
                    card_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retro_card_votes", x => new { x.card_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_retro_card_votes_retro_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "retro_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_retro_card_votes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_retro_card_votes_user_id",
                table: "retro_card_votes",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_retro_cards_author_id",
                table: "retro_cards",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_retro_cards_section_id",
                table: "retro_cards",
                column: "section_id");

            migrationBuilder.CreateIndex(
                name: "ix_retro_participants_user_id",
                table: "retro_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_retro_sections_retro_id_sort_order",
                table: "retro_sections",
                columns: new[] { "retro_id", "sort_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retros_organization_id_created_at",
                table: "retros",
                columns: new[] { "organization_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_retros_owner_id",
                table: "retros",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retro_card_votes");

            migrationBuilder.DropTable(
                name: "retro_participants");

            migrationBuilder.DropTable(
                name: "retro_cards");

            migrationBuilder.DropTable(
                name: "retro_sections");

            migrationBuilder.DropTable(
                name: "retros");
        }
    }
}

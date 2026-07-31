using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_updates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    issue_id = table.Column<long>(type: "bigint", nullable: true),
                    organization_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_updates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "issue_update_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    issue_update_id = table.Column<long>(type: "bigint", nullable: false),
                    old_value_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    new_value_id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    action = table.Column<int>(type: "integer", nullable: false),
                    entity_type = table.Column<int>(type: "integer", nullable: false),
                    old_display_value = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    new_display_value = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    property_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_update_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_update_items_issue_updates_issue_update_id",
                        column: x => x.issue_update_id,
                        principalTable: "issue_updates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_update_items_issue_update_id",
                table: "issue_update_items",
                column: "issue_update_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_updates_organization_id_created_at",
                table: "issue_updates",
                columns: new[] { "organization_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_update_items");

            migrationBuilder.DropTable(
                name: "issue_updates");
        }
    }
}

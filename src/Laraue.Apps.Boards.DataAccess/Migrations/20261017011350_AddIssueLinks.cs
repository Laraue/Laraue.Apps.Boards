using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<int>(type: "integer", nullable: false),
                    source_issue_id = table.Column<long>(type: "bigint", nullable: false),
                    target_issue_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_links", x => x.id);
                    table.CheckConstraint("ck_issue_links_not_self", "source_issue_id <> target_issue_id");
                    table.CheckConstraint("ck_issue_links_relates_to_ordered", "type <> 1 OR source_issue_id < target_issue_id");
                    table.ForeignKey(
                        name: "fk_issue_links_issues_source_issue_id",
                        column: x => x.source_issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_links_issues_target_issue_id",
                        column: x => x.target_issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_links_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_links_created_by_user_id",
                table: "issue_links",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_links_source_issue_id",
                table: "issue_links",
                column: "source_issue_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_links_target_issue_id",
                table: "issue_links",
                column: "target_issue_id");

            // One link per pair of issues, whatever its type and direction (Constraints.IssueLinkPairIndexName).
            // EF Core can't model an expression index, so it isn't in the model snapshot.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ux_issue_links_issue_pair
                ON issue_links (LEAST(source_issue_id, target_issue_id), GREATEST(source_issue_id, target_issue_id))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_links");
        }
    }
}

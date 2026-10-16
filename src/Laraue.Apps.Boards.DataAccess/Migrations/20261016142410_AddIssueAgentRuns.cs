using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueAgentRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_agent_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    issue_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    claimed_by_api_key_id = table.Column<Guid>(type: "uuid", nullable: true),
                    finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_agent_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_agent_runs_api_keys_claimed_by_api_key_id",
                        column: x => x.claimed_by_api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_issue_agent_runs_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_agent_runs_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_agent_runs_claimed_by_api_key_id",
                table: "issue_agent_runs",
                column: "claimed_by_api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_agent_runs_created_by_user_id",
                table: "issue_agent_runs",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_agent_runs_issue_id_id",
                table: "issue_agent_runs",
                columns: new[] { "issue_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_issue_agent_runs_issue_id_open",
                table: "issue_agent_runs",
                column: "issue_id",
                unique: true,
                filter: "state IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ix_issue_agent_runs_state",
                table: "issue_agent_runs",
                column: "state");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_agent_runs");
        }
    }
}

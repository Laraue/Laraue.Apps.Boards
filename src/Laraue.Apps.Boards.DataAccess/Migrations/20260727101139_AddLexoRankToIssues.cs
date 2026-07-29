using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddLexoRankToIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "lexo_rank",
                table: "issues",
                type: "character(34)",
                fixedLength: true,
                maxLength: 34,
                nullable: false,
                defaultValue: "",
                collation: "C");

            // Fill lexo ranks so that the initial order matches issue creation order
            // (lowest id -> highest id) within each organization, spreading ranks
            // evenly across the available lexo rank space. Ranks are alphabetically
            // ordered, so this order will change with every subsequent issue movement.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION pg_temp.b36(v numeric) RETURNS text AS $$
DECLARE d text := ''; r numeric;
BEGIN
  FOR i IN 1..32 LOOP
    r := mod(v, 36); v := div(v, 36);
    d := substr('0123456789abcdefghijklmnopqrstuvwxyz', r::int + 1, 1) || d;
  END LOOP;
  RETURN '0|' || d;
END $$ LANGUAGE plpgsql;");

            migrationBuilder.Sql(@"
WITH issue_order AS (
    SELECT i.id,
           row_number() OVER (PARTITION BY sp.organization_id ORDER BY i.id) AS rn,
           count(*) OVER (PARTITION BY sp.organization_id) AS cnt
    FROM issues i
    JOIN statuses s ON s.id = i.status_id
    JOIN epics e ON e.id = s.epic_id
    JOIN spaces sp ON sp.id = e.space_id
)
UPDATE issues i
SET lexo_rank = pg_temp.b36(div(power(36::numeric, 32) - 1, o.cnt + 1) * o.rn)
FROM issue_order o
WHERE i.id = o.id;");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS pg_temp.b36(numeric);");

            migrationBuilder.CreateIndex(
                name: "ix_issues_lexo_rank",
                table: "issues",
                column: "lexo_rank");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_issues_lexo_rank",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "lexo_rank",
                table: "issues");
        }
    }
}
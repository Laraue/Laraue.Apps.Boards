using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueTitle : Migration
    {
        /// <summary>
        /// Backfills the title from the sanitized first non-empty line of the content: leading
        /// markdown markers (#, >, list bullets/numbers) and emphasis markers (*, `, edge _) are
        /// stripped, links become their text, whitespace is collapsed, and a line longer than 256
        /// characters is cut to 255 plus an ellipsis. Unlike the C# sanitizer it doesn't cut on a
        /// word boundary. Content stays untouched. Public for the tests that run it on seeded data.
        /// </summary>
        public const string BackfillTitleSql = @"
update issues i
set title = case
    when length(t.line) > 256 then left(t.line, 255) || '…'
    else t.line
end
from (
    select id, trim(regexp_replace(
        regexp_replace(
        regexp_replace(
        regexp_replace(
        regexp_replace(
        regexp_replace(
            coalesce((regexp_match(content, '^\s*([^\n]*\S[^\n]*)'))[1], ''),
            '^(\s*(#+|>|[-*+]|\d+[.)])(\s+|$))+', ''),
            '\[([^\]]*)\]\([^)]*\)', '\1', 'g'),
            '[*`]', '', 'g'),
            '(^|\s)_+', '\1', 'g'),
            '_+(\s|$)', '\1', 'g'),
            '\s+', ' ', 'g')) as line
    from issues
) t
where t.id = i.id;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_title_set_explicitly",
                table: "issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "issues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_issues_title",
                table: "issues",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.Sql(BackfillTitleSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_issues_title",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "is_title_set_explicitly",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "title",
                table: "issues");
        }
    }
}

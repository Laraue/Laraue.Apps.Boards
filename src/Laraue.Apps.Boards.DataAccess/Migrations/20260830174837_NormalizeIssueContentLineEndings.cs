using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeIssueContentLineEndings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data-only fix, no schema change: issues.content saved before IssueChange.SetContent
            // started normalizing line endings can still contain "\r\n"/"\r" (from the web app's
            // textarea) alongside issues saved from Telegram (which only ever used "\n"). Collapse
            // everything to "\n" so a future edit's content-equality check in
            // CoreIssuesService.Update doesn't see a "change" that's really just a line-ending
            // mismatch and log a history entry whose old/new values render identically.
            migrationBuilder.Sql(
                """
                UPDATE issues
                SET content = regexp_replace(content, E'\r\n?', E'\n', 'g')
                WHERE content LIKE '%' || chr(13) || '%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible - the original line-ending style per row isn't recoverable once
            // normalized, and there's no reason to want it back.
        }
    }
}

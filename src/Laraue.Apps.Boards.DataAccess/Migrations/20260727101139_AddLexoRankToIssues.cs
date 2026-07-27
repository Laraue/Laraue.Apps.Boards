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

            // Set middle lexo rank for all issues. We use ordering by lexo_rank -> id. So, it will
            // be ordered alphabetically and will be changed with every issue nmovement,
            migrationBuilder.Sql("update issues set lexo_rank = '0|hzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz'");

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

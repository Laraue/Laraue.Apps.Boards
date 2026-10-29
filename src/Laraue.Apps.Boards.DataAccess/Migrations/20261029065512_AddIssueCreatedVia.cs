using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueCreatedVia : Migration
    {
        /// <summary>
        /// Sets <c>created_via</c> for existing issues (all start as Web, 0): Telegram (1) when the issue has a
        /// Telegram message, otherwise from the API key of the issue's create entry in history - Agent (3) when the key
        /// was issued by agent sign-in, Mcp (2) for any other key, which is also stored as <c>created_by_api_key_id</c>.
        /// Public for the tests that run it on seeded data.
        /// </summary>
        public const string BackfillSql = @"
update issues i
set created_via = case
        when i.telegram_message_id is not null then 1
        when exists (
            select 1 from agent_sign_in_requests r
            where r.api_key_id = l.api_key_id) then 3
        else 2
    end,
    created_by_api_key_id = case when i.telegram_message_id is null then l.api_key_id end
from (
    select i2.id as issue_id, l2.api_key_id
    from issues i2
    left join organization_logs l2
        on l2.entity_id = i2.id and l2.entity_type = 0 and l2.action = 0
) l
where l.issue_id = i.id
    and (i.telegram_message_id is not null or l.api_key_id is not null);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_api_key_id",
                table: "issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "created_via",
                table: "issues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_issues_created_by_api_key_id",
                table: "issues",
                column: "created_by_api_key_id");

            migrationBuilder.AddForeignKey(
                name: "fk_issues_api_keys_created_by_api_key_id",
                table: "issues",
                column: "created_by_api_key_id",
                principalTable: "api_keys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_issues_api_keys_created_by_api_key_id",
                table: "issues");

            migrationBuilder.DropIndex(
                name: "ix_issues_created_by_api_key_id",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "created_by_api_key_id",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "created_via",
                table: "issues");
        }
    }
}

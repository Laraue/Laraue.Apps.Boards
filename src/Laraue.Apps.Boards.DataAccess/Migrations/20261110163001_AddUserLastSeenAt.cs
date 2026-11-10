using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastSeenAt : Migration
    {
        /// <summary>
        /// Sets <c>last_seen_at</c> for existing users to their latest change in history (an issue or comment
        /// created, changed or deleted). Users who only read stay null until they are next seen. Public for the
        /// tests that run it on seeded data.
        /// </summary>
        public const string BackfillSql = @"
update users u
set last_seen_at = l.last_seen_at
from (
    select owner_id, max(created_at) as last_seen_at
    from organization_logs
    group by owner_id
) l
where l.owner_id = u.id;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_seen_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "users");
        }
    }
}

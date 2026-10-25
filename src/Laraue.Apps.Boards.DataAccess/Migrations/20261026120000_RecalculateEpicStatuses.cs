using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RecalculateEpicStatuses : Migration
    {
        /// <summary>
        /// Derives every epic's status from its active issues, as <c>RecalculateEpicStatuses</c> does from now
        /// on: New (0) with no issues or all of them Created (category 0), Done (2) with all of them Completed
        /// (category 2), Active (1) otherwise. Epics marked Done by hand with open issues become Active. Public
        /// for the tests that run it on seeded data.
        /// </summary>
        public const string RecalculateSql = @"
update epics e
set status = case
    when not exists (
        select 1 from issues i join statuses s on s.id = i.status_id
        where s.epic_id = e.id and i.deleted_at is null and s.category <> 0) then 0
    when not exists (
        select 1 from issues i join statuses s on s.id = i.status_id
        where s.epic_id = e.id and i.deleted_at is null and s.category <> 2) then 2
    else 1
end;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RecalculateSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

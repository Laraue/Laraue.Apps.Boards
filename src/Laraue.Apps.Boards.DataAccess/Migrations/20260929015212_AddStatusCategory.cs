using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusCategory : Migration
    {
        /// <summary>
        /// Backfills the category by the column position among the epic's active statuses: the
        /// first column is Created (0), the last one Completed (2), everything in between
        /// InProgress (1). An epic with a single status keeps it Created. Soft-deleted statuses
        /// aren't columns anymore and get InProgress. Public for the tests that run it on seeded data.
        /// </summary>
        public const string BackfillCategorySql = @"
update statuses s
set category = case
    when r.position = 1 then 0
    when r.position = r.total then 2
    else 1
end
from (
    select id,
        row_number() over (partition by epic_id order by sort_order, id) as position,
        count(*) over (partition by epic_id) as total
    from statuses
    where deleted_at is null
) r
where r.id = s.id;

update statuses set category = 1 where deleted_at is not null;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "category",
                table: "statuses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(BackfillCategorySql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "category",
                table: "statuses");
        }
    }
}

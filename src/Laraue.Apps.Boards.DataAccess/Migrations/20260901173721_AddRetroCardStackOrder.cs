using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddRetroCardStackOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "stack_order",
                table: "retro_cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Freeze the order the boards are painted in today (oldest at the bottom) so nothing
            // jumps around the first time someone opens an existing retro.
            migrationBuilder.Sql("""
                update retro_cards c
                set stack_order = ordered.rn
                from (
                    select rc.id,
                           row_number() over (partition by s.retro_id order by rc.created_at, rc.id) as rn
                    from retro_cards rc
                    join retro_sections s on s.id = rc.section_id
                ) ordered
                where ordered.id = c.id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stack_order",
                table: "retro_cards");
        }
    }
}

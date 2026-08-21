using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusToEpic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "epics",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                table: "epics");
        }
    }
}

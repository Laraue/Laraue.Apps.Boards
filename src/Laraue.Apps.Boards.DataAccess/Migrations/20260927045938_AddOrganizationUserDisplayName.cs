using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationUserDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "organization_users",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "organization_users",
                type: "character varying(129)",
                maxLength: 129,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initials",
                table: "organization_users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                table: "organization_users");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "organization_users");

            migrationBuilder.DropColumn(
                name: "initials",
                table: "organization_users");
        }
    }
}

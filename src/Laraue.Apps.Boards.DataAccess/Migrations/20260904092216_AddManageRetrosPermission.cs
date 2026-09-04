using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddManageRetrosPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_manage_retros",
                table: "organization_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_retros",
                table: "direct_space_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // organizations.type: 0 = Organization, 1 = Personal (OrganizationType enum) - only
            // owners of non-personal organizations get the manage-retros grant by default.
            migrationBuilder.Sql("""
                UPDATE organization_users
                SET can_manage_retros = TRUE
                FROM organizations
                WHERE organizations.id = organization_users.organization_id
                  AND organizations.owner_id = organization_users.user_id
                  AND organizations.type <> 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "can_manage_retros",
                table: "organization_users");

            migrationBuilder.DropColumn(
                name: "can_manage_retros",
                table: "direct_space_permissions");
        }
    }
}

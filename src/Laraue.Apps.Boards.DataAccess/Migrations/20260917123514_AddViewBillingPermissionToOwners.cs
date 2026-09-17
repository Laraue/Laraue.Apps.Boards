using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddViewBillingPermissionToOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AdminAccessLevel.ViewBilling (bit 64) is new - grant it to every existing
            // organization's owner via bitwise OR, rather than overwriting to AdminAccessLevel.All,
            // in case an owner's row was ever set to something narrower than All.
            migrationBuilder.Sql(
                """
                UPDATE organization_users ou
                SET admin_access_level = ou.admin_access_level | 64
                FROM organizations o
                WHERE ou.organization_id = o.id AND ou.user_id = o.owner_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE organization_users ou
                SET admin_access_level = ou.admin_access_level & ~64
                FROM organizations o
                WHERE ou.organization_id = o.id AND ou.user_id = o.owner_id;
                """);
        }
    }
}

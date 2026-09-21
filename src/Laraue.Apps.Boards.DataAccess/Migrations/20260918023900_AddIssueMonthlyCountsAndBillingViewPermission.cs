using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueMonthlyCountsAndBillingViewPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_monthly_counts",
                columns: table => new
                {
                    organization_id = table.Column<long>(type: "bigint", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_monthly_counts", x => new { x.organization_id, x.year, x.month });
                    table.ForeignKey(
                        name: "fk_issue_monthly_counts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // AdminAccessLevel.ViewBilling (bit 64) is new - grant it to every existing *team*
            // organization's owner via bitwise OR (rather than overwriting to AdminAccessLevel.All,
            // in case an owner's row was ever set to something narrower than All). Personal
            // organizations (OrganizationType.Personal = 1) are excluded on purpose - a personal
            // org has no admin billing ledger to view (see
            // PersonalOrganizationTransactionsNotSupportedException), matching
            // OrganizationDefaults.GetNewOrganizationEntity, which never includes ViewBilling in a
            // personal owner's default AdminAccessLevel either.
            migrationBuilder.Sql(
                """
                UPDATE organization_users ou
                SET admin_access_level = ou.admin_access_level | 64
                FROM organizations o
                WHERE ou.organization_id = o.id AND ou.user_id = o.owner_id AND o.type = 0;
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
                WHERE ou.organization_id = o.id AND ou.user_id = o.owner_id AND o.type = 0;
                """);

            migrationBuilder.DropTable(
                name: "issue_monthly_counts");
        }
    }
}

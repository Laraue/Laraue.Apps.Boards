using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationBillingId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "billing_id",
                table: "organizations",
                type: "uuid",
                nullable: true);

            // Only new organizations get a BillingId assigned in code (OrganizationDefaults) -
            // backfill existing non-personal ones (type = 0) so they don't hit a null BillingId
            // the first time something tries to reserve tokens for them. Personal organizations
            // are intentionally left null - they bill under their owning user's id instead.
            migrationBuilder.Sql("UPDATE organizations SET billing_id = gen_random_uuid() WHERE type = 0;");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_billing_id",
                table: "organizations",
                column: "billing_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organizations_billing_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "billing_id",
                table: "organizations");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class IssuePeriodCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_period_counts",
                columns: table => new
                {
                    organization_id = table.Column<long>(type: "bigint", nullable: false),
                    period_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_period_counts", x => new { x.organization_id, x.period_started_at });
                    table.ForeignKey(
                        name: "fk_issue_period_counts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // The old rows are calendar months: their period starts on the first of the month (UTC), which
            // is the period a paid plan keeps counting in. A Free plan counts in its own rolling month
            // from now on, so its current month starts again from zero.
            migrationBuilder.Sql(@"
                INSERT INTO issue_period_counts (organization_id, period_started_at, count)
                SELECT organization_id, make_timestamptz(year, month, 1, 0, 0, 0, 'UTC'), count
                FROM issue_monthly_counts");

            migrationBuilder.DropTable(
                name: "issue_monthly_counts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            // Periods of one calendar month are added up into that month.
            migrationBuilder.Sql(@"
                INSERT INTO issue_monthly_counts (organization_id, year, month, count)
                SELECT organization_id,
                       EXTRACT(YEAR FROM period_started_at AT TIME ZONE 'UTC')::int,
                       EXTRACT(MONTH FROM period_started_at AT TIME ZONE 'UTC')::int,
                       SUM(count)::int
                FROM issue_period_counts
                GROUP BY 1, 2, 3");

            migrationBuilder.DropTable(
                name: "issue_period_counts");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationSetupCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "setup_completed_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            // Organizations that existed before the setup window are already set up: their people
            // never see it.
            migrationBuilder.Sql("UPDATE organizations SET setup_completed_at = created_at;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "setup_completed_at",
                table: "organizations");
        }
    }
}

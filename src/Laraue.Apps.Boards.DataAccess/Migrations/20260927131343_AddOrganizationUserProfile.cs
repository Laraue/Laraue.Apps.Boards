using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "organization_users",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "organization_users",
                type: "character varying(257)",
                maxLength: 257,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "initials",
                table: "organization_users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "left_at",
                table: "organization_users",
                type: "timestamp with time zone",
                nullable: true);

            // Every current member is shown the way they are today - by the user's own name/color.
            migrationBuilder.Sql("""
                UPDATE organization_users ou
                SET display_name = u.display_name,
                    initials = u.initials,
                    color = u.color
                FROM users u
                WHERE u.id = ou.user_id;
                """);
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

            migrationBuilder.DropColumn(
                name: "left_at",
                table: "organization_users");
        }
    }
}

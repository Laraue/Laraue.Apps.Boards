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

            // Names now live on the membership rows (copied from Laraue.Apps.Identity on joining).
            migrationBuilder.DropColumn(
                name: "color",
                table: "users");

            migrationBuilder.DropColumn(
                name: "display_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "initials",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "users",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "users",
                type: "character varying(129)",
                maxLength: 129,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "initials",
                table: "users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            // A user's name back from their oldest membership, and the former members removed again -
            // before this migration leaving deleted the row.
            migrationBuilder.Sql("""
                UPDATE users u
                SET display_name = left(ou.display_name, 129),
                    initials = ou.initials,
                    color = ou.color
                FROM (
                    SELECT DISTINCT ON (user_id) user_id, display_name, initials, color
                    FROM organization_users
                    ORDER BY user_id, id
                ) ou
                WHERE ou.user_id = u.id;

                DELETE FROM organization_users WHERE left_at IS NOT NULL;
                """);

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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationUserProfileAndLeftAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every member gets their name/color in the organization materialized: what they set
            // there, or else the user's current default - the same thing reads fall back to today.
            migrationBuilder.Sql("""
                UPDATE organization_users ou
                SET display_name = COALESCE(ou.display_name, u.display_name),
                    initials = COALESCE(ou.initials, u.initials),
                    color = COALESCE(ou.color, u.color)
                FROM users u
                WHERE u.id = ou.user_id;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "initials",
                table: "organization_users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2)",
                oldMaxLength: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "organization_users",
                type: "character varying(257)",
                maxLength: 257,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(129)",
                oldMaxLength: 129,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "organization_users",
                type: "character varying(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "left_at",
                table: "organization_users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "left_at",
                table: "organization_users");

            migrationBuilder.AlterColumn<string>(
                name: "initials",
                table: "organization_users",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2)",
                oldMaxLength: 2);

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "organization_users",
                type: "character varying(129)",
                maxLength: 129,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(257)",
                oldMaxLength: 257);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "organization_users",
                type: "character varying(7)",
                maxLength: 7,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(7)",
                oldMaxLength: 7);
        }
    }
}

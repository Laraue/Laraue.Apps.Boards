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

            // Leaving used to delete the membership row. Bring one back, as a former member with no
            // permissions, for everyone still shown somewhere in an organization they left - their
            // issues, comments, history, retros and Telegram messages - so their name keeps showing.
            // When they actually left isn't known; the migration time stands in for it.
            migrationBuilder.Sql("""
                WITH issue_organizations AS (
                    SELECT i.id AS issue_id, i.owner_id, i.assignee_id, i.telegram_message_id, s.organization_id
                    FROM issues i
                    JOIN statuses st ON st.id = i.status_id
                    JOIN epics e ON e.id = st.epic_id
                    JOIN spaces s ON s.id = e.space_id
                ),
                retro_cards_organizations AS (
                    SELECT rc.author_id, rc.assignee_id, r.organization_id
                    FROM retro_cards rc
                    JOIN retro_sections rs ON rs.id = rc.section_id
                    JOIN retros r ON r.id = rs.retro_id
                ),
                shown_users AS (
                    SELECT organization_id, owner_id AS user_id FROM issue_organizations
                    UNION SELECT organization_id, assignee_id FROM issue_organizations
                    UNION SELECT io.organization_id, c.owner_id
                        FROM issue_comments c JOIN issue_organizations io ON io.issue_id = c.issue_id
                    UNION SELECT io.organization_id, tm.sender_id
                        FROM issue_organizations io JOIN telegram_messages tm ON tm.id = io.telegram_message_id
                    UNION SELECT organization_id, owner_id FROM organization_logs
                    UNION SELECT organization_id, owner_id FROM retros
                    UNION SELECT organization_id, author_id FROM retro_cards_organizations
                    UNION SELECT organization_id, assignee_id FROM retro_cards_organizations
                    UNION SELECT r.organization_id, rp.user_id
                        FROM retro_participants rp JOIN retros r ON r.id = rp.retro_id
                )
                INSERT INTO organization_users (
                    organization_id, user_id, display_name, initials, color, left_at, admin_access_level,
                    can_read, can_manage_retros,
                    can_create_spaces, can_update_spaces, can_delete_spaces,
                    can_create_epics, can_update_epics, can_delete_epics,
                    can_create_issues, can_update_issues, can_delete_issues)
                SELECT su.organization_id, su.user_id, u.display_name, u.initials, u.color, now(), 0,
                    false, false,
                    false, false, false,
                    false, false, false,
                    false, false, false
                FROM shown_users su
                JOIN users u ON u.id = su.user_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM organization_users ou
                    WHERE ou.organization_id = su.organization_id AND ou.user_id = su.user_id);
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

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDisplayNameAndInitials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.Sql(
                """
                UPDATE users SET
                    display_name = left(CASE
                        WHEN telegram_user_name IS NOT NULL THEN telegram_user_name
                        WHEN length(telegram_first_name) > 0 AND length(telegram_last_name) > 0
                            THEN telegram_first_name || ' ' || telegram_last_name
                        WHEN length(telegram_first_name) > 1 THEN telegram_first_name
                        WHEN length(telegram_last_name) > 1 THEN telegram_last_name
                        ELSE 'Unknown'
                    END, 129),
                    initials = upper(CASE
                        WHEN telegram_user_name IS NOT NULL AND length(telegram_user_name) > 1 THEN left(telegram_user_name, 2)
                        WHEN telegram_user_name IS NOT NULL THEN ''
                        WHEN length(telegram_first_name) > 0 AND length(telegram_last_name) > 0
                            THEN left(telegram_first_name, 1) || left(telegram_last_name, 1)
                        WHEN length(telegram_first_name) > 1 THEN left(telegram_first_name, 2)
                        WHEN length(telegram_last_name) > 1 THEN left(telegram_last_name, 2)
                        ELSE 'UN'
                    END);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "initials",
                table: "users");
        }
    }
}

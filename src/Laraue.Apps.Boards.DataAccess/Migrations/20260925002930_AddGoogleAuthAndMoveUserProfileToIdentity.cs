using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleAuthAndMoveUserProfileToIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Google sign-in (BRD-214): a user may now have no Telegram account at all.
            migrationBuilder.AlterColumn<long>(
                name: "telegram_id",
                table: "users",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "google_subject",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_google_subject",
                table: "users",
                column: "google_subject",
                unique: true);

            // Laraue.Apps.Identity is now the source of truth for a user's profile (the data was
            // copied there by the BRD-209 script), so Boards keeps only ids + DisplayName/Initials.
            // The one profile value Boards still needs is the interface language, so move it into
            // user_preferences before dropping the column. Unsupported codes are skipped - a null
            // interface_language already falls back to the default language.
            migrationBuilder.AddColumn<string>(
                name: "interface_language",
                table: "user_preferences",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO user_preferences (user_id, epic_sort_order, interface_language)
                SELECT id, 0, telegram_language_code
                FROM users
                WHERE telegram_language_code IN ('en', 'ru')
                ON CONFLICT (user_id) DO UPDATE SET interface_language = EXCLUDED.interface_language;
                """);

            migrationBuilder.DropColumn(
                name: "telegram_first_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "telegram_language_code",
                table: "users");

            migrationBuilder.DropColumn(
                name: "telegram_last_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "telegram_user_name",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_google_subject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "google_subject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "interface_language",
                table: "user_preferences");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_id",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telegram_first_name",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telegram_language_code",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telegram_last_name",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telegram_user_name",
                table: "users",
                type: "text",
                nullable: true);
        }
    }
}

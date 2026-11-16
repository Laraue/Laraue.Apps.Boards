using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class IncreaseContentAndCommentLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "old_display_value",
                table: "organization_log_items",
                type: "character varying(32768)",
                maxLength: 32768,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "new_display_value",
                table: "organization_log_items",
                type: "character varying(32768)",
                maxLength: 32768,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "content",
                table: "issues",
                type: "character varying(32768)",
                maxLength: 32768,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "issue_comments",
                type: "character varying(32768)",
                maxLength: 32768,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4096)",
                oldMaxLength: 4096);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "old_display_value",
                table: "organization_log_items",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32768)",
                oldMaxLength: 32768,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "new_display_value",
                table: "organization_log_items",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32768)",
                oldMaxLength: 32768,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "content",
                table: "issues",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32768)",
                oldMaxLength: 32768,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "text",
                table: "issue_comments",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32768)",
                oldMaxLength: 32768);
        }
    }
}

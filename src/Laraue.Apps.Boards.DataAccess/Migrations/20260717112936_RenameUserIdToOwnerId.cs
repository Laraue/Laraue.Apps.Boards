using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RenameUserIdToOwnerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_issues_users_user_id",
                table: "issues");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "issues",
                newName: "owner_id");

            migrationBuilder.RenameIndex(
                name: "ix_issues_user_id",
                table: "issues",
                newName: "ix_issues_owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_issues_users_owner_id",
                table: "issues",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_issues_users_owner_id",
                table: "issues");

            migrationBuilder.RenameColumn(
                name: "owner_id",
                table: "issues",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "ix_issues_owner_id",
                table: "issues",
                newName: "ix_issues_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_issues_users_user_id",
                table: "issues",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

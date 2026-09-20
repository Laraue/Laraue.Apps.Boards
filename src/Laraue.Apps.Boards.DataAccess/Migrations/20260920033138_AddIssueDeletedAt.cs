using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "issues",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_issues_deleted_by_user_id",
                table: "issues",
                column: "deleted_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_issues_users_deleted_by_user_id",
                table: "issues",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_issues_users_deleted_by_user_id",
                table: "issues");

            migrationBuilder.DropIndex(
                name: "ix_issues_deleted_by_user_id",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "issues");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueCommentDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "issue_comments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "issue_comments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_issue_comments_deleted_by_user_id",
                table: "issue_comments",
                column: "deleted_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_issue_comments_users_deleted_by_user_id",
                table: "issue_comments",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_issue_comments_users_deleted_by_user_id",
                table: "issue_comments");

            migrationBuilder.DropIndex(
                name: "ix_issue_comments_deleted_by_user_id",
                table: "issue_comments");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "issue_comments");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "issue_comments");
        }
    }
}

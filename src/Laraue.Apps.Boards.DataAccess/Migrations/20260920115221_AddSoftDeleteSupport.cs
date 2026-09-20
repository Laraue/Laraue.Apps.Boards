using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_spaces_organization_id_key",
                table: "spaces");

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "statuses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "statuses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "spaces",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "spaces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "organizations",
                type: "uuid",
                nullable: true);

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

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "epics",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "epics",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_statuses_deleted_by_user_id",
                table: "statuses",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_deleted_by_user_id",
                table: "spaces",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_organization_id_key",
                table: "spaces",
                columns: new[] { "organization_id", "key" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_organizations_deleted_by_user_id",
                table: "organizations",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_issues_deleted_by_user_id",
                table: "issues",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_comments_deleted_by_user_id",
                table: "issue_comments",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_epics_deleted_by_user_id",
                table: "epics",
                column: "deleted_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_epics_users_deleted_by_user_id",
                table: "epics",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_issue_comments_users_deleted_by_user_id",
                table: "issue_comments",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_issues_users_deleted_by_user_id",
                table: "issues",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_organizations_users_deleted_by_user_id",
                table: "organizations",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_spaces_users_deleted_by_user_id",
                table: "spaces",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_statuses_users_deleted_by_user_id",
                table: "statuses",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_epics_users_deleted_by_user_id",
                table: "epics");

            migrationBuilder.DropForeignKey(
                name: "fk_issue_comments_users_deleted_by_user_id",
                table: "issue_comments");

            migrationBuilder.DropForeignKey(
                name: "fk_issues_users_deleted_by_user_id",
                table: "issues");

            migrationBuilder.DropForeignKey(
                name: "fk_organizations_users_deleted_by_user_id",
                table: "organizations");

            migrationBuilder.DropForeignKey(
                name: "fk_spaces_users_deleted_by_user_id",
                table: "spaces");

            migrationBuilder.DropForeignKey(
                name: "fk_statuses_users_deleted_by_user_id",
                table: "statuses");

            migrationBuilder.DropIndex(
                name: "ix_statuses_deleted_by_user_id",
                table: "statuses");

            migrationBuilder.DropIndex(
                name: "ix_spaces_deleted_by_user_id",
                table: "spaces");

            migrationBuilder.DropIndex(
                name: "ix_spaces_organization_id_key",
                table: "spaces");

            migrationBuilder.DropIndex(
                name: "ix_organizations_deleted_by_user_id",
                table: "organizations");

            migrationBuilder.DropIndex(
                name: "ix_issues_deleted_by_user_id",
                table: "issues");

            migrationBuilder.DropIndex(
                name: "ix_issue_comments_deleted_by_user_id",
                table: "issue_comments");

            migrationBuilder.DropIndex(
                name: "ix_epics_deleted_by_user_id",
                table: "epics");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "statuses");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "statuses");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "spaces");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "spaces");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "issue_comments");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "issue_comments");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "epics");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "epics");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_organization_id_key",
                table: "spaces",
                columns: new[] { "organization_id", "key" },
                unique: true);
        }
    }
}

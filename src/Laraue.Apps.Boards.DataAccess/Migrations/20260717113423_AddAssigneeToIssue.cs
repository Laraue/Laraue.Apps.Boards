using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAssigneeToIssue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "assignee_id",
                table: "issues",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
            
            migrationBuilder.Sql("update issues set assignee_id = owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_issues_assignee_id",
                table: "issues",
                column: "assignee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_issues_users_assignee_id",
                table: "issues",
                column: "assignee_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_issues_users_assignee_id",
                table: "issues");

            migrationBuilder.DropIndex(
                name: "ix_issues_assignee_id",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "assignee_id",
                table: "issues");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "ix_organizations_deleted_by_user_id",
                table: "organizations",
                column: "deleted_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_organizations_users_deleted_by_user_id",
                table: "organizations",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_organizations_users_deleted_by_user_id",
                table: "organizations");

            migrationBuilder.DropIndex(
                name: "ix_organizations_deleted_by_user_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "organizations");
        }
    }
}

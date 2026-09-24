using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyIdToOrganizationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "api_key_id",
                table: "organization_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_organization_logs_api_key_id",
                table: "organization_logs",
                column: "api_key_id");

            migrationBuilder.AddForeignKey(
                name: "fk_organization_logs_api_keys_api_key_id",
                table: "organization_logs",
                column: "api_key_id",
                principalTable: "api_keys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_organization_logs_api_keys_api_key_id",
                table: "organization_logs");

            migrationBuilder.DropIndex(
                name: "ix_organization_logs_api_key_id",
                table: "organization_logs");

            migrationBuilder.DropColumn(
                name: "api_key_id",
                table: "organization_logs");
        }
    }
}

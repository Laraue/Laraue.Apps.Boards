using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSignInReplacesApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "replaces_api_key_id",
                table: "agent_sign_in_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_sign_in_requests_replaces_api_key_id",
                table: "agent_sign_in_requests",
                column: "replaces_api_key_id");

            migrationBuilder.AddForeignKey(
                name: "fk_agent_sign_in_requests_api_keys_replaces_api_key_id",
                table: "agent_sign_in_requests",
                column: "replaces_api_key_id",
                principalTable: "api_keys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_agent_sign_in_requests_api_keys_replaces_api_key_id",
                table: "agent_sign_in_requests");

            migrationBuilder.DropIndex(
                name: "ix_agent_sign_in_requests_replaces_api_key_id",
                table: "agent_sign_in_requests");

            migrationBuilder.DropColumn(
                name: "replaces_api_key_id",
                table: "agent_sign_in_requests");
        }
    }
}

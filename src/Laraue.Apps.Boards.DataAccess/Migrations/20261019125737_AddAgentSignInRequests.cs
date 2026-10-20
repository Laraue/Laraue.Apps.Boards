using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSignInRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_sign_in_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_code = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    machine_name = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_polled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_sign_in_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_sign_in_requests_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_agent_sign_in_requests_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_agent_sign_in_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_sign_in_requests_api_key_id",
                table: "agent_sign_in_requests",
                column: "api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_sign_in_requests_device_code_hash",
                table: "agent_sign_in_requests",
                column: "device_code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_sign_in_requests_organization_id",
                table: "agent_sign_in_requests",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_sign_in_requests_user_code",
                table: "agent_sign_in_requests",
                column: "user_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_sign_in_requests_user_id",
                table: "agent_sign_in_requests",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_sign_in_requests");
        }
    }
}

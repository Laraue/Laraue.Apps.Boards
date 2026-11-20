using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthAuthorizationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "oauth_authorization_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    client_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    redirect_uri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    client_state = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    code_challenge = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    resource = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    state = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    organization_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_authorization_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_oauth_authorization_requests_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_oauth_authorization_requests_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_oauth_authorization_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorization_requests_api_key_id",
                table: "oauth_authorization_requests",
                column: "api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorization_requests_code_hash",
                table: "oauth_authorization_requests",
                column: "code_hash",
                unique: true,
                filter: "code_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorization_requests_organization_id_user_id_client",
                table: "oauth_authorization_requests",
                columns: new[] { "organization_id", "user_id", "client_id" },
                filter: "api_key_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_oauth_authorization_requests_user_id",
                table: "oauth_authorization_requests",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oauth_authorization_requests");
        }
    }
}

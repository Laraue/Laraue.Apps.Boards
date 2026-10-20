using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentSignInClientName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "machine_name",
                table: "agent_sign_in_requests",
                type: "character varying(37)",
                maxLength: 37,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(44)",
                oldMaxLength: 44);

            migrationBuilder.AddColumn<string>(
                name: "client_name",
                table: "agent_sign_in_requests",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "client_name",
                table: "agent_sign_in_requests");

            migrationBuilder.AlterColumn<string>(
                name: "machine_name",
                table: "agent_sign_in_requests",
                type: "character varying(44)",
                maxLength: 44,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(37)",
                oldMaxLength: 37);
        }
    }
}

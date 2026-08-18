using Laraue.Apps.Boards.DataAccess;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations;

[DbContext(typeof(DatabaseContext))]
[Migration("20260813090000_AddUserOnboarding")]
public class AddUserOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_onboardings",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                onboarding_id = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<int>(type: "integer", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_onboardings", x => new { x.user_id, x.onboarding_id });
                table.ForeignKey(
                    name: "fk_user_onboardings_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_onboardings");
    }
}

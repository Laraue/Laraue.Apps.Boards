using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added nullable first so existing rows can each get their own generated id (not a
            // shared default) before the column is locked down to NOT NULL below.
            migrationBuilder.AddColumn<Guid>(
                name: "global_user_id",
                table: "users",
                type: "uuid",
                nullable: true);

            // Users created before this column existed get a locally-generated id here -
            // gen_random_uuid() is Postgres-core since v13, no extension needed. New users get a
            // real one from Laraue.Apps.Identity via CoreUserService before this ever runs again.
            migrationBuilder.Sql("UPDATE users SET global_user_id = gen_random_uuid() WHERE global_user_id IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "global_user_id",
                table: "users",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "global_user_id",
                table: "users");
        }
    }
}

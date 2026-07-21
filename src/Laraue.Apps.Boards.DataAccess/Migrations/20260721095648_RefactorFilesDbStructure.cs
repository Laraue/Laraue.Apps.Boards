using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RefactorFilesDbStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attachments_issues_issue_id",
                table: "attachments");

            migrationBuilder.DropTable(
                name: "image_attachments");

            migrationBuilder.DropIndex(
                name: "ix_attachments_issue_id",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "mime_type",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "name",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "size",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "content_type",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "file_name",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "issue_id",
                table: "attachments");

            migrationBuilder.RenameColumn(
                name: "file_unique_id",
                table: "telegram_files",
                newName: "external_file_unique_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_files_file_unique_id",
                table: "telegram_files",
                newName: "ix_telegram_files_external_file_unique_id");

            migrationBuilder.AlterColumn<long>(
                name: "thumbnail_file_id",
                table: "telegram_message_videos",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "file_id",
                table: "telegram_message_videos",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<long>(
                name: "telegram_file_id",
                table: "telegram_message_photos",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "file_id",
                table: "telegram_files",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<long>(
                name: "id",
                table: "telegram_files",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<string>(
                name: "external_file_id",
                table: "telegram_files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "file_id",
                table: "attachments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "preview_file_id",
                table: "attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "type",
                table: "attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    mime_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "issue_attachments",
                columns: table => new
                {
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_attachments", x => new { x.issue_id, x.attachment_id });
                    table.ForeignKey(
                        name: "fk_issue_attachments_attachments_attachment_id",
                        column: x => x.attachment_id,
                        principalTable: "attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_attachments_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_files_file_id",
                table: "telegram_files",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attachments_file_id",
                table: "attachments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_preview_file_id",
                table: "attachments",
                column: "preview_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attachments_attachment_id",
                table: "issue_attachments",
                column: "attachment_id");

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_files_file_id",
                table: "attachments",
                column: "file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_files_preview_file_id",
                table: "attachments",
                column: "preview_file_id",
                principalTable: "files",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_files_files_file_id",
                table: "telegram_files",
                column: "file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attachments_files_file_id",
                table: "attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_attachments_files_preview_file_id",
                table: "attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_files_files_file_id",
                table: "telegram_files");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "issue_attachments");

            migrationBuilder.DropIndex(
                name: "ix_telegram_files_file_id",
                table: "telegram_files");

            migrationBuilder.DropIndex(
                name: "ix_attachments_file_id",
                table: "attachments");

            migrationBuilder.DropIndex(
                name: "ix_attachments_preview_file_id",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "external_file_id",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "file_id",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "preview_file_id",
                table: "attachments");

            migrationBuilder.DropColumn(
                name: "type",
                table: "attachments");

            migrationBuilder.RenameColumn(
                name: "external_file_unique_id",
                table: "telegram_files",
                newName: "file_unique_id");

            migrationBuilder.RenameIndex(
                name: "ix_telegram_files_external_file_unique_id",
                table: "telegram_files",
                newName: "ix_telegram_files_file_unique_id");

            migrationBuilder.AlterColumn<Guid>(
                name: "thumbnail_file_id",
                table: "telegram_message_videos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "file_id",
                table: "telegram_message_videos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<Guid>(
                name: "telegram_file_id",
                table: "telegram_message_photos",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "file_id",
                table: "telegram_files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                table: "telegram_files",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<string>(
                name: "mime_type",
                table: "telegram_files",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "telegram_files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size",
                table: "telegram_files",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                table: "attachments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "file_name",
                table: "attachments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "issue_id",
                table: "attachments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "image_attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_telegram_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    thumbnail_telegram_file_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_image_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_image_attachments_attachments_attachment_id",
                        column: x => x.attachment_id,
                        principalTable: "attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_image_attachments_telegram_files_original_telegram_file_id",
                        column: x => x.original_telegram_file_id,
                        principalTable: "telegram_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_image_attachments_telegram_files_thumbnail_telegram_file_id",
                        column: x => x.thumbnail_telegram_file_id,
                        principalTable: "telegram_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_issue_id",
                table: "attachments",
                column: "issue_id");

            migrationBuilder.CreateIndex(
                name: "ix_image_attachments_attachment_id",
                table: "image_attachments",
                column: "attachment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_image_attachments_original_telegram_file_id",
                table: "image_attachments",
                column: "original_telegram_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_image_attachments_thumbnail_telegram_file_id",
                table: "image_attachments",
                column: "thumbnail_telegram_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_issues_issue_id",
                table: "attachments",
                column: "issue_id",
                principalTable: "issues",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}

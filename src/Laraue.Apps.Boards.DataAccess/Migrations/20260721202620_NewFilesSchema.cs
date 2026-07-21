using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class NewFilesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_photos");

            migrationBuilder.DropTable(
                name: "telegram_videos");

            migrationBuilder.DropIndex(
                name: "ix_telegram_files_file_unique_id",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "mime_type",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "name",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "size",
                table: "telegram_files");

            migrationBuilder.RenameColumn(
                name: "file_unique_id",
                table: "telegram_files",
                newName: "external_file_unique_id");

            migrationBuilder.AddColumn<Guid>(
                name: "attachment_id",
                table: "telegram_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "file_id",
                table: "telegram_files");
            
            migrationBuilder.AddColumn<Guid>(
                name: "file_id",
                table: "telegram_files",
                type: "uuid",
                nullable: false);
            
            migrationBuilder.DropPrimaryKey(
                name: "pk_telegram_files",
                table: "telegram_files");
            
            migrationBuilder.DropColumn(
                name: "id",
                table: "telegram_files");
            
            migrationBuilder.AddColumn<long>(
                name: "id",
                table: "telegram_files",
                nullable: false)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
            
            migrationBuilder.AddPrimaryKey(
                name: "PK_telegram_files",
                table: "telegram_files",
                column: "id");

            migrationBuilder.AddColumn<string>(
                name: "external_file_id",
                table: "telegram_files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

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
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preview_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_attachments_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attachments_files_preview_file_id",
                        column: x => x.preview_file_id,
                        principalTable: "files",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_attachments_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "ix_telegram_messages_attachment_id",
                table: "telegram_messages",
                column: "attachment_id");

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
                name: "ix_attachments_owner_id",
                table: "attachments",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_preview_file_id",
                table: "attachments",
                column: "preview_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attachments_attachment_id",
                table: "issue_attachments",
                column: "attachment_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_files_files_file_id",
                table: "telegram_files",
                column: "file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id",
                table: "telegram_messages",
                column: "attachment_id",
                principalTable: "attachments",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_telegram_files_files_file_id",
                table: "telegram_files");

            migrationBuilder.DropForeignKey(
                name: "fk_telegram_messages_attachments_attachment_id",
                table: "telegram_messages");

            migrationBuilder.DropTable(
                name: "issue_attachments");

            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropIndex(
                name: "ix_telegram_messages_attachment_id",
                table: "telegram_messages");

            migrationBuilder.DropIndex(
                name: "ix_telegram_files_file_id",
                table: "telegram_files");

            migrationBuilder.DropColumn(
                name: "attachment_id",
                table: "telegram_messages");

            migrationBuilder.DropColumn(
                name: "external_file_id",
                table: "telegram_files");

            migrationBuilder.RenameColumn(
                name: "external_file_unique_id",
                table: "telegram_files",
                newName: "file_unique_id");

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

            migrationBuilder.CreateTable(
                name: "telegram_photos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    telegram_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_message_id = table.Column<long>(type: "bigint", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    photo_type = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_photos", x => x.id);
                    table.ForeignKey(
                        name: "fk_telegram_photos_telegram_files_telegram_file_id",
                        column: x => x.telegram_file_id,
                        principalTable: "telegram_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_telegram_photos_telegram_messages_telegram_message_id",
                        column: x => x.telegram_message_id,
                        principalTable: "telegram_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_videos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_message_id = table.Column<long>(type: "bigint", nullable: false),
                    thumbnail_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: false),
                    thumbnail_height = table.Column<int>(type: "integer", nullable: true),
                    thumbnail_width = table.Column<int>(type: "integer", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_videos", x => x.id);
                    table.ForeignKey(
                        name: "fk_telegram_videos_telegram_files_file_id",
                        column: x => x.file_id,
                        principalTable: "telegram_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_telegram_videos_telegram_files_thumbnail_file_id",
                        column: x => x.thumbnail_file_id,
                        principalTable: "telegram_files",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_telegram_videos_telegram_messages_telegram_message_id",
                        column: x => x.telegram_message_id,
                        principalTable: "telegram_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_files_file_unique_id",
                table: "telegram_files",
                column: "file_unique_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_photos_telegram_file_id",
                table: "telegram_photos",
                column: "telegram_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_photos_telegram_message_id",
                table: "telegram_photos",
                column: "telegram_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_videos_file_id",
                table: "telegram_videos",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_videos_telegram_message_id",
                table: "telegram_videos",
                column: "telegram_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_videos_thumbnail_file_id",
                table: "telegram_videos",
                column: "thumbnail_file_id");
        }
    }
}

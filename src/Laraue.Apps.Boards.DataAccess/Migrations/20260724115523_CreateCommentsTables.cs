using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Laraue.Apps.StructuredMessages.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class CreateCommentsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_comments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_comments_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_comments_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issue_comments_attachments",
                columns: table => new
                {
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_comments_attachments", x => new { x.comment_id, x.attachment_id });
                    table.ForeignKey(
                        name: "fk_issue_comments_attachments_attachments_attachment_id",
                        column: x => x.attachment_id,
                        principalTable: "attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_comments_attachments_issue_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "issue_comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_comments_issue_id",
                table: "issue_comments",
                column: "issue_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_comments_owner_id",
                table: "issue_comments",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_comments_attachments_attachment_id",
                table: "issue_comments_attachments",
                column: "attachment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_comments_attachments");

            migrationBuilder.DropTable(
                name: "issue_comments");
        }
    }
}

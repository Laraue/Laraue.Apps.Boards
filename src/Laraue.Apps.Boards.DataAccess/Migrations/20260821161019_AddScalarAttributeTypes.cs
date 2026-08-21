using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddScalarAttributeTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "text",
                table: "issue_attribute_text_values",
                newName: "value");

            migrationBuilder.RenameIndex(
                name: "ix_issue_attribute_text_values_text",
                table: "issue_attribute_text_values",
                newName: "ix_issue_attribute_text_values_value");

            migrationBuilder.CreateTable(
                name: "issue_attribute_date_time_values",
                columns: table => new
                {
                    issue_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_attribute_date_time_values", x => new { x.issue_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_issue_attribute_date_time_values_attributes_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attributes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_attribute_date_time_values_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issue_attribute_date_values",
                columns: table => new
                {
                    issue_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_attribute_date_values", x => new { x.issue_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_issue_attribute_date_values_attributes_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attributes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_attribute_date_values_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issue_attribute_decimal_values",
                columns: table => new
                {
                    issue_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_attribute_decimal_values", x => new { x.issue_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_issue_attribute_decimal_values_attributes_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attributes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_attribute_decimal_values_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "issue_attribute_integer_values",
                columns: table => new
                {
                    issue_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_issue_attribute_integer_values", x => new { x.issue_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_issue_attribute_integer_values_attributes_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attributes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_issue_attribute_integer_values_issues_issue_id",
                        column: x => x.issue_id,
                        principalTable: "issues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_date_time_values_attribute_id",
                table: "issue_attribute_date_time_values",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_date_time_values_value",
                table: "issue_attribute_date_time_values",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_date_values_attribute_id",
                table: "issue_attribute_date_values",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_date_values_value",
                table: "issue_attribute_date_values",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_decimal_values_attribute_id",
                table: "issue_attribute_decimal_values",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_decimal_values_value",
                table: "issue_attribute_decimal_values",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_integer_values_attribute_id",
                table: "issue_attribute_integer_values",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "ix_issue_attribute_integer_values_value",
                table: "issue_attribute_integer_values",
                column: "value");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_attribute_date_time_values");

            migrationBuilder.DropTable(
                name: "issue_attribute_date_values");

            migrationBuilder.DropTable(
                name: "issue_attribute_decimal_values");

            migrationBuilder.DropTable(
                name: "issue_attribute_integer_values");

            migrationBuilder.RenameColumn(
                name: "value",
                table: "issue_attribute_text_values",
                newName: "text");

            migrationBuilder.RenameIndex(
                name: "ix_issue_attribute_text_values_value",
                table: "issue_attribute_text_values",
                newName: "ix_issue_attribute_text_values_text");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAttributeSettings : Migration
    {
        /// <summary>
        /// Existing attributes stay enabled and shown on cards (true defaults above); their order within the
        /// organization follows creation, as the attribute list does today.
        /// </summary>
        private const string BackfillSortOrderSql = @"
update attributes a
set sort_order = o.sort_order
from (
    select id, row_number() over (partition by organization_id order by id) - 1 as sort_order
    from attributes) o
where a.id = o.id";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_enabled",
                table: "attributes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "show_on_card",
                table: "attributes",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "attributes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(BackfillSortOrderSql);

            migrationBuilder.CreateTable(
                name: "epic_attribute_settings",
                columns: table => new
                {
                    epic_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    show_on_card = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epic_attribute_settings", x => new { x.epic_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_epic_attribute_settings_attributes_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attributes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_epic_attribute_settings_epics_epic_id",
                        column: x => x.epic_id,
                        principalTable: "epics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "space_attribute_settings",
                columns: table => new
                {
                    space_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    show_on_card = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_space_attribute_settings", x => new { x.space_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_space_attribute_settings_attributes_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "attributes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_space_attribute_settings_spaces_space_id",
                        column: x => x.space_id,
                        principalTable: "spaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_epic_attribute_settings_attribute_id",
                table: "epic_attribute_settings",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "ix_space_attribute_settings_attribute_id",
                table: "space_attribute_settings",
                column: "attribute_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "epic_attribute_settings");

            migrationBuilder.DropTable(
                name: "space_attribute_settings");

            migrationBuilder.DropColumn(
                name: "is_enabled",
                table: "attributes");

            migrationBuilder.DropColumn(
                name: "show_on_card",
                table: "attributes");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "attributes");
        }
    }
}

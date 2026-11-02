using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCrossOrganizationAttributeValues : Migration
    {
        /// <summary>
        /// Moving a space or an epic to another organization used to leave its issues' attribute values behind,
        /// pointing to the old organization's attributes (BRD-179). Deletes every value whose attribute isn't the
        /// issue's organization's. Public for the test that runs it on seeded data.
        /// </summary>
        public const string CleanupSql = @"
delete from issue_attribute_text_values v
using issues i, statuses st, epics e, spaces s, attributes a
where i.id = v.issue_id and st.id = i.status_id and e.id = st.epic_id and s.id = e.space_id
    and a.id = v.attribute_id and a.organization_id <> s.organization_id;
delete from issue_attribute_list_values v
using issues i, statuses st, epics e, spaces s, attributes a
where i.id = v.issue_id and st.id = i.status_id and e.id = st.epic_id and s.id = e.space_id
    and a.id = v.attribute_id and a.organization_id <> s.organization_id;
delete from issue_attribute_integer_values v
using issues i, statuses st, epics e, spaces s, attributes a
where i.id = v.issue_id and st.id = i.status_id and e.id = st.epic_id and s.id = e.space_id
    and a.id = v.attribute_id and a.organization_id <> s.organization_id;
delete from issue_attribute_decimal_values v
using issues i, statuses st, epics e, spaces s, attributes a
where i.id = v.issue_id and st.id = i.status_id and e.id = st.epic_id and s.id = e.space_id
    and a.id = v.attribute_id and a.organization_id <> s.organization_id;
delete from issue_attribute_date_values v
using issues i, statuses st, epics e, spaces s, attributes a
where i.id = v.issue_id and st.id = i.status_id and e.id = st.epic_id and s.id = e.space_id
    and a.id = v.attribute_id and a.organization_id <> s.organization_id;
delete from issue_attribute_date_time_values v
using issues i, statuses st, epics e, spaces s, attributes a
where i.id = v.issue_id and st.id = i.status_id and e.id = st.epic_id and s.id = e.space_id
    and a.id = v.attribute_id and a.organization_id <> s.organization_id;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CleanupSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

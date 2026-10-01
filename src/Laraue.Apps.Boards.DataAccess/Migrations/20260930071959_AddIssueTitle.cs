using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Laraue.Apps.Boards.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueTitle : Migration
    {
        /// <summary>
        /// Backfills the title from the sanitized first non-empty line of the content: leading markdown
        /// markers (#, >, list bullets/numbers) and emphasis markers (*, `, edge _) are stripped, links
        /// become their text, whitespace is collapsed, and a line longer than 256 characters is cut to 255
        /// plus an ellipsis. Unlike the C# sanitizer it doesn't cut on a word boundary.
        /// A line with several sentences (a period followed by a space and more text) gives only its first
        /// sentence, without the period; "1.2" or "example.com" are not sentence ends.
        /// Leading "---" rule lines are dropped from the content first, so neither the title nor the
        /// description starts with one.
        /// When the first line is itself a markdown title - a "# Heading", or a line followed by a "---" line
        /// (the "title, rule, text" layout the AI save used to store; blank lines before the rule are fine) -
        /// it moves out of the description: the line (and the rule) is removed from the content, which becomes
        /// null when nothing else is left. A line with several sentences stays in the description whole, so no
        /// text is lost. A description that is exactly the title, so it would only repeat it, is emptied (null).
        /// Markdown the title doesn't carry - emphasis, a link - keeps the description, and so does a title cut
        /// to 256 characters. Any other content stays as it is.
        /// The history of issue descriptions gets the same treatment, value by value, so the history agrees
        /// with the descriptions: a description change that only touched the title line becomes a title change,
        /// and a creation entry whose description is now empty is dropped. Comments are not touched.
        /// All of it runs through one temporary function. Public for the tests that run it on seeded data.
        /// </summary>
        public const string BackfillTitleSql = @"
create or replace function pg_temp.split_issue_title(source text)
returns table(title text, description text)
language sql
as $$
select
    case
        when length(u.title) > 256 then left(u.title, 255) || '…'
        else u.title
    end,
    case
        when u.title <> '' and length(u.title) <= 256
            and regexp_replace(u.new_content, '^\s+|\s+$', '', 'g') = u.title then null
        else u.new_content
    end
from (
    select
        case
            when s.is_multi then trim(coalesce(nullif(substring(s.line from '^(.+?)\.\s'), ''), s.line))
            else s.line
        end as title,
        case
            when s.is_multi then s.kept_content
            when s.first_line ~ '^#+[ \t]'
                then nullif(regexp_replace(regexp_replace(s.rest, '^\s+', ''), '^(\s*---+[ \t]*(\n|$))+\s*', ''), '')
            when s.rest ~ '^\s*---+[ \t]*(\n|$)'
                then nullif(regexp_replace(
                    regexp_replace(regexp_replace(s.rest, '^\s*---+[ \t]*(\n|$)', ''), '^\s+', ''),
                    '^(\s*---+[ \t]*(\n|$))+\s*', ''), '')
            else s.kept_content
        end as new_content
    from (
        select q.*, (q.line ~ '\.\s+\S') as is_multi
        from (
            select r.first_line, r.rest,
                case when r.content = r.original_content then r.content else nullif(r.content, '') end as kept_content,
                trim(regexp_replace(
                    regexp_replace(
                    regexp_replace(
                    regexp_replace(
                    regexp_replace(
                    regexp_replace(
                        r.first_line,
                        '^(\s*(#+|>|[-*+]|\d+[.)])(\s+|$))+', ''),
                        '\[([^\]]*)\]\([^)]*\)', '\1', 'g'),
                        '[*`]', '', 'g'),
                        '(^|\s)_+', '\1', 'g'),
                        '_+(\s|$)', '\1', 'g'),
                        '\s+', ' ', 'g')) as line
            from (
                select x.content, x.original_content,
                    coalesce(x.m[2], '') as first_line,
                    case
                        when x.m is null then ''
                        else substr(x.content, length(x.m[1]) + length(x.m[2]) + length(x.m[3]) + 1)
                    end as rest
                from (
                    select content, original_content,
                        regexp_match(content, '^(\s*)([^\n]*\S[^\n]*)(\n|$)') as m
                    from (
                        select source as original_content,
                            regexp_replace(source, '^(\s*---+[ \t]*(\n|$))+\s*', '') as content
                    ) b
                ) x
            ) r
        ) q
    ) s
) u;
$$;

update issues i
set title = r.title,
    content = r.description
from (
    select x.id, f.title, f.description
    from issues x
    cross join lateral pg_temp.split_issue_title(x.content) f
) r
where r.id = i.id;

update organization_log_items li
set property_type = 7,
    old_display_value = c.old_title,
    new_display_value = c.new_title
from (
    select h.id, o.title as old_title, n.title as new_title
    from organization_log_items h
    join organization_logs l on l.id = h.organization_log_id
    cross join lateral pg_temp.split_issue_title(h.old_display_value) o
    cross join lateral pg_temp.split_issue_title(h.new_display_value) n
    where l.entity_type = 0
        and h.property_type = 1
        and h.old_display_value is not null
        and o.description is not distinct from n.description
        and o.title <> n.title
) c
where c.id = li.id;

update organization_log_items li
set old_display_value = d.old_description,
    new_display_value = d.new_description
from (
    select h.id, o.description as old_description, n.description as new_description
    from organization_log_items h
    join organization_logs l on l.id = h.organization_log_id
    cross join lateral pg_temp.split_issue_title(h.old_display_value) o
    cross join lateral pg_temp.split_issue_title(h.new_display_value) n
    where l.entity_type = 0
        and h.property_type = 1
) d
where d.id = li.id;

delete from organization_log_items li
using organization_logs l
where l.id = li.organization_log_id
    and l.entity_type = 0
    and li.property_type = 1
    and nullif(li.old_display_value, '') is null
    and nullif(li.new_display_value, '') is null;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_title_set_explicitly",
                table: "issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "issues",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_issues_title",
                table: "issues",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.Sql(BackfillTitleSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_issues_title",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "is_title_set_explicitly",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "title",
                table: "issues");
        }
    }
}

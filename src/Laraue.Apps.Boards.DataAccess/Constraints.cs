namespace Laraue.Apps.Boards.DataAccess;

public class Constraints
{
    public const int MaxCommentLength = 4096;

    public const int MaxTitleLength = 256;

    public const int MaxContentLength = 4096;

    public const string SpaceKeyIndexName = "ix_spaces_organization_id_key";

    /// <summary>
    /// The unique index allowing one <see cref="Models.IssueLink"/> per pair of issues, whichever is the
    /// source. An expression index (LEAST/GREATEST of the two ids), created by raw SQL in the
    /// AddIssueLinks migration, since EF Core can't model one.
    /// </summary>
    public const string IssueLinkPairIndexName = "ux_issue_links_issue_pair";
}
namespace Laraue.Apps.Boards.DataAccess;

public class Constraints
{
    public const int MaxCommentLength = 4096;

    public const int MaxTitleLength = 256;

    public const int MaxContentLength = 4096;

    public const string SpaceKeyIndexName = "ix_spaces_organization_id_key";
}
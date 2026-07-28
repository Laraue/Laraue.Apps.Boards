namespace Laraue.Apps.Boards.DataAccess.Models;

public class OrganizationMaxSortOrder
{
    public long OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int MaxSortOrder { get; set; }
}
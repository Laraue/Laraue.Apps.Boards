namespace Laraue.Apps.Boards.Services;

public interface IOrganizationIssueSortOrderCounter
{
    Task<int> GetNextNumber(long organizationId, CancellationToken cancellationToken);
}

public class OrganizationIssueSortOrderCounter : IOrganizationIssueSortOrderCounter
{
    public Task<int> GetNextNumber(long organizationId, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
using System.Linq.Expressions;

namespace Laraue.Apps.Boards.Services.Sorting;

public static class SortingExtensions
{
    public static IQueryable<T> ApplySorting<T, TKey>(
        this IQueryable<T> query,
        Expression<Func<T, TKey>> selector,
        SortingDirection direction)
    {
        return direction switch
        {
            SortingDirection.Ascending => query.OrderBy(selector),
            SortingDirection.Descending => query.OrderByDescending(selector),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }
}
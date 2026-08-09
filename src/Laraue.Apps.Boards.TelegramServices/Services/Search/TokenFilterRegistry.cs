namespace Laraue.Apps.Boards.TelegramServices.Services.Search;

/// <summary>
/// Looks up the right <see cref="IQueryTokenFilter"/> for a token key ("org", "assignee", ...).
/// Register filters in DI as IQueryTokenFilter implementations; this class collects them.
/// Adding a new filter (e.g. "status:") means writing one class and registering it —
/// no changes needed here or in the controller.
/// </summary>
public interface ITokenFilterRegistry
{
    bool TryGet(string key, out IQueryTokenFilter filter);
    IReadOnlySet<string> Keys { get; }
}

public sealed class TokenFilterRegistry : ITokenFilterRegistry
{
    private readonly IReadOnlyDictionary<string, IQueryTokenFilter> _filtersByKey;
 
    public TokenFilterRegistry(IEnumerable<IQueryTokenFilter> filters)
    {
        _filtersByKey = filters.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
    }
 
    public bool TryGet(string key, out IQueryTokenFilter filter) =>
        _filtersByKey.TryGetValue(key, out filter!);
 
    public IReadOnlySet<string> Keys => _filtersByKey.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
}

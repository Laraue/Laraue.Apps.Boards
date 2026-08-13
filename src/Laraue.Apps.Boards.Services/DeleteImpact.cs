namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Side effects of a delete a caller may want to warn about before confirming.
/// </summary>
public record DeleteImpact(int AffectedLinkedChatsCount);

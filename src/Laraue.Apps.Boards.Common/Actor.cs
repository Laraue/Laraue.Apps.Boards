namespace Laraue.Apps.Boards.Common;

/// <summary>
/// Who performed a mutation, for attribution on audit/history entries - <see cref="UserId"/> is
/// who to credit, <see cref="ApiKeyId"/> is which API key (if any) they were authenticated with
/// when they did it. Implicitly convertible from <see cref="Guid"/> (the common "no API key"
/// case), so the many existing call sites that only ever have a bare user id don't need to change.
/// </summary>
public readonly record struct Actor(Guid UserId, Guid? ApiKeyId = null)
{
    public static implicit operator Actor(Guid userId) => new(userId);
}

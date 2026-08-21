namespace Laraue.Apps.Boards.Services;

/// <summary>
/// Distinguishes "this property was never touched" from "this property was explicitly set to
/// its default/empty value" - e.g. clearing all attributes on an issue update still needs to be
/// recorded as a change, even though the resulting list is empty. A plain nullable can't tell
/// these apart when the value's own default (null, empty list, ...) is itself a meaningful value.
/// </summary>
public readonly struct ChangedValue<T>
{
    private readonly T _value;

    public bool IsSet { get; }

    private ChangedValue(T value, bool isSet)
    {
        _value = value;
        IsSet = isSet;
    }

    public static ChangedValue<T> Unset { get; } = new(default!, isSet: false);

    public static ChangedValue<T> Of(T value) => new(value, isSet: true);

    public T Value => IsSet
        ? _value
        : throw new InvalidOperationException($"No {typeof(T).Name} value was set.");

    public T GetValueOrDefault(T fallback = default!) => IsSet ? _value : fallback;
}

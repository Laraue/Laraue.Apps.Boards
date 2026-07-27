using System.Numerics;

namespace Laraue.Apps.Boards.Services;

/// <summary>
/// A fixed-width, lexicographically sortable rank for global task ordering.
///
/// Storage format:
///     "{bucket}|{fixed-width base36 value}"
///
/// Example:
///     "0|000000000000000000hzzzzzzzzzzzzzzz"
///
/// Guarantees:
/// - StringComparer.Ordinal order is identical to numeric order.
/// - PostgreSQL ORDER BY rank COLLATE "C" produces the same order.
/// - Between() returns a strictly interior rank or throws.
/// - GenNext()/GenPrev() use a fixed step when possible and gracefully
///   fall back to bisecting the remaining boundary space.
/// - Absolute minimum and maximum values are never exposed as normal ranks.
///
/// Notes:
/// - Concurrent inserts between the same two neighbors can generate the same rank.
///   Enforce UNIQUE (organization_id, rank COLLATE "C") in PostgreSQL and retry
///   the move after re-reading neighbors on unique_violation.
/// - Bucket switching requires organization-level generation/bucket coordination.
///   This type only validates and sorts the bucket; it does not implement migration.
/// </summary>
public sealed class LexoRank : IComparable<LexoRank>, IEquatable<LexoRank>
{
    public const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";
    public const int Radix = 36;
    public const int ValueLength = 32;

    public const char MinBucket = '0';
    public const char MaxBucket = '2';

    private static readonly BigInteger AbsoluteMin = BigInteger.Zero;

    private static readonly BigInteger AbsoluteMax =
        BigInteger.Pow(new BigInteger(Radix), ValueLength) - BigInteger.One;

    private static readonly BigInteger MiddleNumericValue = AbsoluteMax / 2;

    /// <summary>
    /// Used for ordinary append/prepend operations.
    /// When this step does not fit near a boundary, the remaining range is bisected.
    /// </summary>
    private static readonly BigInteger EdgeStep = BigInteger.One << 40;

    public char Bucket { get; }
    public string Value { get; }

    private readonly BigInteger _numericValue;

    private LexoRank(char bucket, BigInteger numericValue)
    {
        ValidateBucket(bucket);

        if (numericValue <= AbsoluteMin || numericValue >= AbsoluteMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numericValue),
                "A stored rank must be strictly inside the absolute rank boundaries.");
        }

        Bucket = bucket;
        _numericValue = numericValue;
        Value = EncodeFixedWidth(numericValue);
    }

    private LexoRank(char bucket, string value, BigInteger numericValue)
    {
        ValidateBucket(bucket);

        if (numericValue <= AbsoluteMin || numericValue >= AbsoluteMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numericValue),
                "A stored rank must be strictly inside the absolute rank boundaries.");
        }

        Bucket = bucket;
        Value = value;
        _numericValue = numericValue;
    }

    public static LexoRank Middle(char bucket = MinBucket) =>
        new(bucket, MiddleNumericValue);

    public static LexoRank Parse(string rank)
    {
        if (!TryParse(rank, out var result))
            throw new FormatException($"Invalid LexoRank string: '{rank}'.");

        return result!;
    }

    public static bool TryParse(string? rank, out LexoRank? result)
    {
        result = null;

        if (rank is null ||
            rank.Length != ValueLength + 2 ||
            rank[1] != '|')
        {
            return false;
        }

        var bucket = rank[0];
        if (!IsValidBucket(bucket))
            return false;

        var value = rank.AsSpan(2, ValueLength);
        if (!TryDecode(value, out var numericValue))
            return false;

        // Absolute boundaries are reserved and cannot be stored as task positions.
        if (numericValue <= AbsoluteMin || numericValue >= AbsoluteMax)
            return false;

        result = new LexoRank(bucket, value.ToString(), numericValue);
        return true;
    }

    public override string ToString() => $"{Bucket}|{Value}";

    /// <summary>
    /// Generates a rank strictly greater than this rank.
    /// </summary>
    public LexoRank GenNext()
    {
        if (_numericValue >= AbsoluteMax - BigInteger.One)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists after the current rank. Rebalance the affected range.");
        }

        var remaining = AbsoluteMax - _numericValue;

        var increment = remaining > EdgeStep
            ? EdgeStep
            : remaining / 2;

        if (increment <= BigInteger.Zero)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists after the current rank. Rebalance the affected range.");
        }

        var candidate = _numericValue + increment;

        if (candidate >= AbsoluteMax)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists after the current rank. Rebalance the affected range.");
        }

        return new LexoRank(Bucket, candidate);
    }

    /// <summary>
    /// Generates a rank strictly smaller than this rank.
    /// </summary>
    public LexoRank GenPrev()
    {
        if (_numericValue <= AbsoluteMin + BigInteger.One)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists before the current rank. Rebalance the affected range.");
        }

        var available = _numericValue - AbsoluteMin;

        var decrement = available > EdgeStep
            ? EdgeStep
            : available / 2;

        if (decrement <= BigInteger.Zero)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists before the current rank. Rebalance the affected range.");
        }

        var candidate = _numericValue - decrement;

        if (candidate <= AbsoluteMin)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists before the current rank. Rebalance the affected range.");
        }

        return new LexoRank(Bucket, candidate);
    }

    /// <summary>
    /// Generates a rank strictly between this rank and <paramref name="other"/>.
    /// The current rank must be smaller than <paramref name="other"/>.
    /// </summary>
    public LexoRank Between(LexoRank other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Bucket != other.Bucket)
        {
            throw new InvalidOperationException(
                "Cannot generate a rank between different buckets.");
        }

        if (_numericValue >= other._numericValue)
        {
            throw new ArgumentException(
                "The current rank must be strictly smaller than the other rank.",
                nameof(other));
        }

        var distance = other._numericValue - _numericValue;

        if (distance <= BigInteger.One)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists between the supplied ranks. Rebalance a local range.");
        }

        var midpoint = _numericValue + distance / 2;

        if (midpoint <= _numericValue || midpoint >= other._numericValue)
        {
            throw new RankSpaceExhaustedException(
                "No representable rank exists between the supplied ranks. Rebalance a local range.");
        }

        return new LexoRank(Bucket, midpoint);
    }

    /// <summary>
    /// Computes a rank from nullable global neighbors.
    /// Null means a true global boundary, not a page boundary.
    /// </summary>
    public static LexoRank Between(
        LexoRank? previous,
        LexoRank? next,
        char bucket = MinBucket)
    {
        ValidateBucket(bucket);

        if (previous is not null && previous.Bucket != bucket)
            throw new InvalidOperationException("Previous rank belongs to another bucket.");

        if (next is not null && next.Bucket != bucket)
            throw new InvalidOperationException("Next rank belongs to another bucket.");

        return (previous, next) switch
        {
            (null, null) => Middle(bucket),
            (null, not null) => next.GenPrev(),
            (not null, null) => previous.GenNext(),
            (not null, not null) => previous.Between(next)
        };
    }

    /// <summary>
    /// Creates strictly increasing, evenly spaced ranks for a full rebalance.
    /// Absolute boundaries are excluded.
    /// </summary>
    public static LexoRank[] CreateEvenlySpaced(
        int count,
        char bucket = MinBucket)
    {
        ValidateBucket(bucket);

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count == 0)
            return Array.Empty<LexoRank>();

        var denominator = new BigInteger(count) + BigInteger.One;
        var step = AbsoluteMax / denominator;

        if (step <= BigInteger.Zero)
        {
            throw new RankSpaceExhaustedException(
                "Too many ranks requested for the configured width.");
        }

        var result = new LexoRank[count];

        for (var i = 0; i < count; i++)
        {
            var numericValue = step * (i + 1);

            if (numericValue <= AbsoluteMin || numericValue >= AbsoluteMax)
            {
                throw new RankSpaceExhaustedException(
                    "Unable to create a valid evenly spaced rank sequence.");
            }

            result[i] = new LexoRank(bucket, numericValue);
        }

        return result;
    }

    /// <summary>
    /// Creates strictly increasing ranks inside an existing open interval.
    /// Useful for local-window rebalance.
    /// </summary>
    public static LexoRank[] CreateEvenlySpacedBetween(
        LexoRank lowerExclusive,
        LexoRank upperExclusive,
        int count)
    {
        ArgumentNullException.ThrowIfNull(lowerExclusive);
        ArgumentNullException.ThrowIfNull(upperExclusive);

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count == 0)
            return Array.Empty<LexoRank>();

        if (lowerExclusive.Bucket != upperExclusive.Bucket)
        {
            throw new InvalidOperationException(
                "Cannot rebalance between ranks from different buckets.");
        }

        if (lowerExclusive._numericValue >= upperExclusive._numericValue)
        {
            throw new ArgumentException(
                "The lower rank must be strictly smaller than the upper rank.");
        }

        var distance = upperExclusive._numericValue - lowerExclusive._numericValue;
        var denominator = new BigInteger(count) + BigInteger.One;
        var step = distance / denominator;

        if (step <= BigInteger.Zero)
        {
            throw new RankSpaceExhaustedException(
                "The supplied interval is too small for the requested rank count.");
        }

        var result = new LexoRank[count];
        var previousValue = lowerExclusive._numericValue;

        for (var i = 0; i < count; i++)
        {
            var numericValue =
                lowerExclusive._numericValue + step * (i + 1);

            if (numericValue <= previousValue ||
                numericValue >= upperExclusive._numericValue)
            {
                throw new RankSpaceExhaustedException(
                    "The supplied interval is too small for the requested rank count.");
            }

            result[i] = new LexoRank(lowerExclusive.Bucket, numericValue);
            previousValue = numericValue;
        }

        return result;
    }

    public int CompareTo(LexoRank? other)
    {
        if (other is null)
            return 1;

        var bucketComparison = Bucket.CompareTo(other.Bucket);
        if (bucketComparison != 0)
            return bucketComparison;

        return string.CompareOrdinal(Value, other.Value);
    }

    public bool Equals(LexoRank? other) =>
        other is not null &&
        Bucket == other.Bucket &&
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is LexoRank other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Bucket, Value);

    public static bool operator <(LexoRank left, LexoRank right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) < 0;
    }

    public static bool operator >(LexoRank left, LexoRank right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) > 0;
    }

    public static bool operator <=(LexoRank left, LexoRank right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) <= 0;
    }

    public static bool operator >=(LexoRank left, LexoRank right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return left.CompareTo(right) >= 0;
    }

    public static bool operator ==(LexoRank? left, LexoRank? right) =>
        Equals(left, right);

    public static bool operator !=(LexoRank? left, LexoRank? right) =>
        !Equals(left, right);

    private static string EncodeFixedWidth(BigInteger value)
    {
        if (value < AbsoluteMin || value > AbsoluteMax)
            throw new ArgumentOutOfRangeException(nameof(value));

        Span<char> buffer = stackalloc char[ValueLength];
        var current = value;

        for (var i = ValueLength - 1; i >= 0; i--)
        {
            current = BigInteger.DivRem(current, Radix, out var remainder);
            buffer[i] = Alphabet[(int)remainder];
        }

        if (current != BigInteger.Zero)
            throw new ArgumentOutOfRangeException(nameof(value));

        return new string(buffer);
    }

    private static bool TryDecode(
        ReadOnlySpan<char> value,
        out BigInteger result)
    {
        result = BigInteger.Zero;

        if (value.Length != ValueLength)
            return false;

        foreach (var character in value)
        {
            var digit = DecodeDigit(character);
            if (digit < 0)
                return false;

            result = result * Radix + digit;
        }

        return result >= AbsoluteMin && result <= AbsoluteMax;
    }

    private static int DecodeDigit(char value)
    {
        if (value is >= '0' and <= '9')
            return value - '0';

        if (value is >= 'a' and <= 'z')
            return value - 'a' + 10;

        return -1;
    }

    private static bool IsValidBucket(char bucket) =>
        bucket is >= MinBucket and <= MaxBucket;

    private static void ValidateBucket(char bucket)
    {
        if (!IsValidBucket(bucket))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bucket),
                bucket,
                $"Bucket must be between '{MinBucket}' and '{MaxBucket}'.");
        }
    }
}

/// <summary>
/// Indicates that no representable fixed-width rank exists in the requested interval.
/// Enqueue a local rebalance and retry the operation after it completes.
/// </summary>
public sealed class RankSpaceExhaustedException : InvalidOperationException
{
    public RankSpaceExhaustedException(string message)
        : base(message)
    {
    }
}

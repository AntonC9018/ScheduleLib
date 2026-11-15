using System.Collections.Immutable;

namespace ScheduleLib.Helper;

public readonly struct SequenceComparableImmutableArray<T> : IEquatable<SequenceComparableImmutableArray<T>>
{
    public readonly ImmutableArray<T> Array;

    public SequenceComparableImmutableArray(ImmutableArray<T> array)
    {
        Array = array;
    }

    public bool Equals(SequenceComparableImmutableArray<T> other)
    {
        bool isOtherDefault = other.Array == default;
        bool isSelfDefault = Array == default;

        if (isSelfDefault && isOtherDefault)
        {
            return true;
        }

        if (isSelfDefault || isOtherDefault)
        {
            return false;
        }

        if (Array.SequenceEqual(other.Array))
        {
            return true;
        }

        return false;
    }

    public override bool Equals(object? other)
    {
        if (other is not SequenceComparableImmutableArray<T> arr)
        {
            return false;
        }

        if (Equals(arr))
        {
            return true;
        }
        return false;
    }

    public static bool operator !=(SequenceComparableImmutableArray<T> a, SequenceComparableImmutableArray<T> b) =>
        !(a == b);

    public static bool operator ==(SequenceComparableImmutableArray<T> a, SequenceComparableImmutableArray<T> b) =>
        a.Equals(b);

    public override int GetHashCode()
    {
        if (Array.IsDefault)
        {
            return 0;
        }

        var hash = new HashCode();
        foreach (var item in Array)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }
}

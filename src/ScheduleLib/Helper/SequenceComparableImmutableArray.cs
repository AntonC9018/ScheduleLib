using System.Collections.Immutable;

namespace ScheduleLib.Helper;

public readonly struct SequenceComparableImmutableArray<T> : IEquatable<SequenceComparableImmutableArray<T>>
{
    public readonly ImmutableArray<T> Array;

    public SequenceComparableImmutableArray(ImmutableArray<T> array)
    {
        Array = array;
    }

    public bool Equals(SequenceComparableImmutableArray<T> other) => Array.SequenceEqual(other.Array);
    public override bool Equals(object? other)
    {
        if (other is not SequenceComparableImmutableArray<T> arr)
        {
            return false;
        }
        return this == arr;
    }
    public static bool operator!=(SequenceComparableImmutableArray<T> a, SequenceComparableImmutableArray<T> b) => !(a == b);
    public static bool operator==(SequenceComparableImmutableArray<T> a, SequenceComparableImmutableArray<T> b) => Equals(a, b);
    public override int GetHashCode() => Array.GetHashCode();
}

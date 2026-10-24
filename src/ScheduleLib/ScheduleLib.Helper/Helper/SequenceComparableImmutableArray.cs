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
        bool conditionResult;
        if (isSelfDefault)
        {
            conditionResult = isOtherDefault;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
        {
            return true;
        }
        bool conditionResult2;
        if (isSelfDefault)
        {
            conditionResult2 = true;
        }
        else
        {
            conditionResult2 = isOtherDefault;
        }

        if (conditionResult2)
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

        if (this == arr)
        {
            return true;
        }

        return false;
    }
    public static bool operator!=(SequenceComparableImmutableArray<T> a, SequenceComparableImmutableArray<T> b) => !(a == b);
    public static bool operator==(SequenceComparableImmutableArray<T> a, SequenceComparableImmutableArray<T> b) => Equals(objA: a, objB: b);
    public override int GetHashCode() => Array.GetHashCode();
}

using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public sealed class SequenceComparableImmutableArrayTests
{
    [Fact]
    public void EqualSequencesHaveEqualHashes()
    {
        var first = new SequenceComparableImmutableArray<int>(ImmutableArray.Create(item1: 1, item2: 2));
        var second = new SequenceComparableImmutableArray<int>(ImmutableArray.Create(item1: 1, item2: 2));

        Assert.True(first.Equals(second));
        Assert.Equal(expected: first.GetHashCode(), actual: second.GetHashCode());
    }

    [Fact]
    public void EqualityWorksThroughOperatorsObjectsAndHashSets()
    {
        var first = new SequenceComparableImmutableArray<int>(ImmutableArray.Create(item1: 1, item2: 2));
        var second = new SequenceComparableImmutableArray<int>(ImmutableArray.Create(item1: 1, item2: 2));
        var reversed = new SequenceComparableImmutableArray<int>(ImmutableArray.Create(item1: 2, item2: 1));
        object boxedSecond = second;
        object boxedReversed = reversed;

        Assert.True(first == second);
        Assert.True(first.Equals(boxedSecond));
        Assert.True(first != reversed);
        Assert.False(first.Equals(boxedReversed));
        var set = new HashSet<SequenceComparableImmutableArray<int>> { first };
        Assert.Contains(second, set);
        Assert.DoesNotContain(reversed, set);
    }

    [Fact]
    public void DefaultAndEmptyRemainDistinct()
    {
        SequenceComparableImmutableArray<int> first = default;
        SequenceComparableImmutableArray<int> second = default;
        var empty = new SequenceComparableImmutableArray<int>(ImmutableArray<int>.Empty);
        object boxedSecond = second;

        Assert.True(first == second);
        Assert.True(first.Equals(boxedSecond));
        Assert.Equal(expected: first.GetHashCode(), actual: second.GetHashCode());
        Assert.True(first != empty);
        Assert.False(first.Equals(null));
        Assert.False(first.Equals(new object()));
    }
}

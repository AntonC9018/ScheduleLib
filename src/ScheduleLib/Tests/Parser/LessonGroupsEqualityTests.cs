using ScheduleLib;

public sealed class LessonGroupsEqualityTests
{
    [Fact]
    public void UnusedStorageDoesNotAffectValueHashing()
    {
        var first = new LessonGroups();
        first.Add(new GroupId(7));
        var second = first;
        second[3] = new GroupId(42);

        Assert.True(first.Equals(second));
        Assert.Equal(expected: first.GetHashCode(), actual: second.GetHashCode());
        var groups = new HashSet<LessonGroups> { first };
        Assert.Contains(second, groups);
    }
}

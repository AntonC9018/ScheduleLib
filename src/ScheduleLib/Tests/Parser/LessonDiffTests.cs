using ScheduleLib.Builders;

namespace ScheduleLib.ParserTests;

public sealed class LessonDiffTests
{
    [Fact]
    public void EqualityOperatorOnDiffMaskIsCorrect()
    {
        var a = new LessonModelDiffMask
        {
            Day = true,
        };
        var b = new LessonModelDiffMask
        {
            AllGroups = true,
        };
        Assert.NotEqual(expected: a, actual: b);
    }

    [Fact]
    public void DiffTwoLessons()
    {
        var lesson1 = new LessonBuilderModelDataBase
        {
            General = new()
            {
                Room = new("423"),
                Course = new(1),
            },
        };
        var lesson2 = new LessonBuilderModelDataBase
        {
            General = new()
            {
                Room = new("423"),
                Course = new(2),
            },
        };
        var diff = LessonBuilderHelper.Diff(a: lesson1, b: lesson2, new()
        {
            Room = true,
            Course = true,
        });
        Assert.True(diff.Course);
        Assert.False(diff.Room);
    }

    [Fact]
    public void MergeTeachers()
    {
        var lesson1 = new LessonBuilderModelDataBase
        {
            General = new()
            {
                Teachers = [new(1), new(2)],
            },
        };
        var lesson2 = new LessonBuilderModelDataBase
        {
            General = new()
            {
                Teachers = [new(2), new(3)],
            },
        };
        LessonBuilderHelper.Merge(to: ref lesson1, @from: lesson2, new()
        {
            Teachers = true,
        });
        Assert.Equal(expected: [new(1), new(2), new(3)], actual: lesson1.General.Teachers);
    }

    [Fact]
    public void MergeGroups()
    {
        var lesson1 = new LessonBuilderModelDataBase
        {
            Group = new()
            {
                Groups = [new(1), new(2)],
            },
        };
        var lesson2 = new LessonBuilderModelDataBase
        {
            Group = new()
            {
                Groups = [new(2), new(3)],
            },
        };
        LessonBuilderHelper.Merge(to: ref lesson1, @from: lesson2, new()
        {
            Groups = true,
        });

        var g = lesson1.Group.Groups;
        global::ScheduleLib.GroupId expected2 = new GroupId(1);
        global::ScheduleLib.GroupId actual2 = g[0];
        Assert.Equal(expected: expected2, actual: actual2);
        global::ScheduleLib.GroupId expected3 = new GroupId(2);
        global::ScheduleLib.GroupId actual3 = g[1];
        Assert.Equal(expected: expected3, actual: actual3);
        global::ScheduleLib.GroupId expected4 = new GroupId(3);
        global::ScheduleLib.GroupId actual4 = g[2];
        Assert.Equal(expected: expected4, actual: actual4);
    }
}

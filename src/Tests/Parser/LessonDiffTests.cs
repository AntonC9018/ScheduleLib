using ScheduleLib;
using ScheduleLib.Builders;

namespace App.Tests;

public sealed class LessonDiffTests
{
    [Fact]
    public void EqualityOperatorOnDiffMaskIsCorrect()
    {
        var a = new RegularLessonModelDiffMask
        {
            Day = true,
        };
        var b = new RegularLessonModelDiffMask
        {
            AllGroups = true,
        };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DiffTwoLessons()
    {
        var lesson1 = new RegularLessonBuilderModelData
        {
            General = new()
            {
                Room = new("423"),
                Course = new(1),
            },
        };
        var lesson2 = new RegularLessonBuilderModelData
        {
            General = new()
            {
                Room = new("423"),
                Course = new(2),
            },
        };
        var diff = LessonBuilderHelper.Diff(lesson1, lesson2, new()
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
        var lesson1 = new RegularLessonBuilderModelData
        {
            General = new()
            {
                Teachers = [new(1), new(2)],
            },
        };
        var lesson2 = new RegularLessonBuilderModelData
        {
            General = new()
            {
                Teachers = [new(2), new(3)],
            },
        };
        LessonBuilderHelper.Merge(ref lesson1, lesson2, new()
        {
            Teachers = true,
        });
        Assert.Equal([new(1), new(2), new(3)], lesson1.General.Teachers);
    }

    [Fact]
    public void MergeGroups()
    {
        var lesson1 = new RegularLessonBuilderModelData
        {
            Group = new()
            {
                Groups = new()
                {
                    Group0 = new(1),
                    Group1 = new(2),
                },
            },
        };
        var lesson2 = new RegularLessonBuilderModelData
        {
            Group = new()
            {
                Groups = new()
                {
                    Group0 = new(2),
                    Group1 = new(3),
                },
            },
        };
        LessonBuilderHelper.Merge(ref lesson1, lesson2, new()
        {
            Groups = true,
        });

        var g = lesson1.Group.Groups;
        Assert.Equal(new GroupId(1), g.Group0);
        Assert.Equal(new GroupId(2), g.Group1);
        Assert.Equal(new GroupId(3), g.Group2);
    }
}

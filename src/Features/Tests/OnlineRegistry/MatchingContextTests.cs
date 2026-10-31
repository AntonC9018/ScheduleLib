using ScheduleLib.OnlineRegistry.Impl;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class MatchingContextTests
{
    private static DateTime DT1
    {
        get
        {
            return new(
                year: 2025,
                month: 1,
                day: 1);
        }
    }

    private static DateTime DT2
    {
        get
        {
            return new(
                year: 2025,
                month: 1,
                day: 2);
        }
    }

    private static DateTime DT3
    {
        get
        {
            return new(
                year: 2025,
                month: 1,
                day: 3);
        }
    }

    private static DateTime DT4
    {
        get
        {
            return new(
                year: 2025,
                month: 1,
                day: 4);
        }
    }

    private static DateTime DT5
    {
        get
        {
            return new(
                year: 2025,
                month: 1,
                day: 5);
        }
    }

    private static DateTime DT6
    {
        get
        {
            return new(
                year: 2025,
                month: 1,
                day: 6);
        }
    }

    [Fact]
    public void BasicCombinationsTest()
    {
        var lists = new MatchingLists();
        lists.AddAll(DT1);
        lists.AddAll(DT2);
        lists.AddExisting(DT2);
        lists.AddExisting(DT3);
        var context = lists.CreateContext();

        var e = context.IteratePotentialMappings()
            .GetEnumerator();
        e.CheckNext(all: DT1, existing: DT2);
        e.CheckNext(all: DT1, existing: DT3);
        e.CheckNext(all: DT2, existing: DT2);
        e.CheckNext(all: DT2, existing: DT3);
        e.CheckLast();
    }

    [Fact]
    public void OnePairUsedUpTest()
    {
        var lists = new MatchingLists();
        lists.AddAll(DT1);
        lists.AddAll(DT2);
        lists.AddAll(DT3);
        lists.AddExisting(DT4);
        lists.AddExisting(DT5);
        lists.AddExisting(DT6);

        var context = lists.CreateContext();

        Mapping CalculateM()
        {
            Mapping m = new(
                AllIndex: 1,
                ExistingIndex: 2);
            return m;
        }

        context.UseUpMatch(CalculateM());

        var e = context.IteratePotentialMappings()
            .GetEnumerator();
        e.CheckNext(all: DT1, existing: DT4);
        e.CheckNext(all: DT1, existing: DT5);
        e.CheckNext(all: DT3, existing: DT4);
        e.CheckNext(all: DT3, existing: DT5);
        e.CheckLast();
    }

    [Fact]
    public void SomeUsedUpWhileIterating()
    {
        var lists = new MatchingLists();
        lists.AddAll(DT1);
        lists.AddAll(DT2);
        lists.AddAll(DT3);
        lists.AddExisting(DT4);
        lists.AddExisting(DT5);
        lists.AddExisting(DT6);

        var context = lists.CreateContext();

        {
            var e = context.IteratePotentialMappings()
                .GetEnumerator();
            e.CheckNext(all: DT1, existing: DT4);
            context.UseUpMatch(e.Current.Mapping);
            e.CheckNext(all: DT2, existing: DT5);
            e.CheckNext(all: DT2, existing: DT6);
            e.CheckNext(all: DT3, existing: DT5);
            context.UseUpMatch(e.Current.Mapping);
            e.CheckLast();
        }

        {
            var e = context.IteratePotentialMappings()
                .GetEnumerator();
            e.CheckNext(all: DT2, existing: DT6);
            e.CheckLast();
        }
    }

    [Fact]
    public void EmptyAllWorks()
    {
        var lists = new MatchingLists();
        lists.AddExisting(DT1);

        var context = lists.CreateContext();

        var e = context.IteratePotentialMappings()
            .GetEnumerator();
        e.CheckLast();
    }

    [Fact]
    public void EmptyExistingWorks()
    {
        var lists = new MatchingLists();
        lists.AddAll(DT1);

        var context = lists.CreateContext();

        var e = context.IteratePotentialMappings()
            .GetEnumerator();
        e.CheckLast();
    }
}

file static class Extensions
{
    public static void AddAll(this MatchingLists lists, DateTime dt)
    {
        lists.AllToday.Add(new()
        {
            LessonId = default,
            DateTime = dt,
            Attendance = [],
            Topic = "",
            RegistryLessonType = LessonType.Lab,
        });
    }

    public static void AddExisting(this MatchingLists lists, DateTime dt)
    {
        lists.ExistingToday.Add(new()
        {
            LessonType = LessonType.Lab,
            DateTime = dt,
            EditUri = default!,
            ViewUri = default!,
            Attendance = [],
            Topic = "",
        });
    }

    public static void CheckNext(
        this ref MatchingContext.PotentialMappingEnumerator e,
        DateTime all,
        DateTime existing)
    {
        Assert.True(e.MoveNext());
        Assert.Equal(expected: e.Current.Data.Local.DateTime, actual: all);
        Assert.Equal(expected: e.Current.Data.Remote.DateTime, actual: existing);
    }

    public static void CheckLast(
        this ref MatchingContext.PotentialMappingEnumerator e)
    {
        Assert.False(e.MoveNext());
    }
}

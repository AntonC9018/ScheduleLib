namespace ScheduleLib.OnlineRegistry.Tests;

public class MatchingContextTests
{
    private DateTime DT1 => new(2025, 1, 1);
    private DateTime DT2 => new(2025, 1, 2);
    private DateTime DT3 => new(2025, 1, 3);
    private DateTime DT4 => new(2025, 1, 4);
    private DateTime DT5 => new(2025, 1, 5);
    private DateTime DT6 => new(2025, 1, 5);

    [Fact]
    public void BasicCombinationsTest()
    {
        var lists = new MatchingLists();
        lists.AddAll(DT1);
        lists.AddAll(DT2);
        lists.AddExisting(DT2);
        lists.AddExisting(DT3);
        var context = lists.CreateContext();

        var e = context.IteratePotentialMappings().GetEnumerator();
        e.CheckNext(DT1, DT2);
        e.CheckNext(DT1, DT3);
        e.CheckNext(DT2, DT2);
        e.CheckNext(DT2, DT3);
        e.CheckLast();
    }

    [Fact]
    public void APairUsedUpTest()
    {
        var lists = new MatchingLists();
        lists.AddAll(DT1);
        lists.AddAll(DT2);
        lists.AddAll(DT3);
        lists.AddExisting(DT4);
        lists.AddExisting(DT5);
        lists.AddExisting(DT6);

        var context = lists.CreateContext();
        context.UseUpMatch(new(1, 2));

        var e = context.IteratePotentialMappings().GetEnumerator();
        e.CheckNext(DT1, DT4);
        e.CheckNext(DT1, DT5);
        e.CheckNext(DT3, DT4);
        e.CheckNext(DT3, DT5);
        e.CheckLast();
    }

    [Fact]
    public void FirstUsedUpWhileIterating()
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
            var e = context.IteratePotentialMappings().GetEnumerator();
            e.CheckNext(DT1, DT4);
            context.UseUpMatch(e.Current.Mapping);
            e.CheckNext(DT2, DT5);
            e.CheckNext(DT2, DT6);
            e.CheckNext(DT3, DT5);
            context.UseUpMatch(e.Current.Mapping);
            e.CheckLast();
        }

        {
            var e = context.IteratePotentialMappings().GetEnumerator();
            e.CheckNext(DT2, DT6);
            e.CheckLast();
        }
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
        });
    }

    public static void CheckNext(
        this ref MatchingContext.PotentialMappingEnumerator e,
        DateTime all,
        DateTime existing)
    {
        Assert.True(e.MoveNext());
        Assert.Equal(e.Current.All.DateTime, all);
        Assert.Equal(e.Current.Existing.DateTime, existing);
    }

    public static void CheckLast(
        this ref MatchingContext.PotentialMappingEnumerator e)
    {
        Assert.False(e.MoveNext());
    }
}


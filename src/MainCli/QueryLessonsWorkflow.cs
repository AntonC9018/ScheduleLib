using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

public sealed record LessonQuery(
    string? Room = null,
    DayOfWeek? Day = null,
    Parity? Parity = null,
    int? BeforeSlot = null)
{
    public static LessonQuery From(QueryArguments arguments)
    {
        return new(
            Room: arguments.Room,
            Day: arguments.Day,
            Parity: arguments.Parity,
            BeforeSlot: arguments.BeforeSlot);
    }

    public bool IsValid
    {
        get
        {
            if (BeforeSlot is < 1 or > 7)
            {
                return false;
            }

            if (Day is { } day && !Enum.IsDefined(day))
            {
                return false;
            }

            if (Parity is { } parity && !Enum.IsDefined(parity))
            {
                return false;
            }

            return true;
        }
    }
}

public sealed class QueryLessonsWorkflow(Schedule schedule, LessonTimeConfig time, LessonQuery query)
{
    public static void Register(IServiceCollection services) => services.AddSeed<LessonQuery>();

    public IEnumerable<LessonResult> Run(CancellationToken cancellationToken)
    {
        if (query.Room is { } room && !schedule.EnumerateWeeklyLessons()
                .Any(x => x.Lesson.Room.Id == room))
        {
            throw new UnknownRoomException(room);
        }

        return Enumerate(cancellationToken);
    }

    private IEnumerable<LessonResult> Enumerate(CancellationToken cancellationToken)
    {
        var latest = schedule.Filter(FilterHelper.Builder()
            .WithLatestPeriod(schedule));
        foreach (var lesson in latest.EnumerateWeeklyLessons())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Matches(lesson))
            {
                yield return ToResult(lesson);
            }
        }
    }

    private bool Matches(WeeklyLessonAccessor lesson)
    {
        if (query.Room is { } room && lesson.Lesson.Room.Id != room)
        {
            return false;
        }

        if (query.Day is { } day && lesson.Date.DayOfWeek != day)
        {
            return false;
        }

        if (query.Parity is { } parity && !lesson.Date.Parity.IsMatch(parity))
        {
            return false;
        }

        if (query.BeforeSlot is { } lastSlot && lesson.Date.TimeSlot.Index + 1 > lastSlot)
        {
            return false;
        }

        return true;
    }

    private LessonResult ToResult(WeeklyLessonAccessor lesson)
    {
        var interval = time.GetTimeSlotInterval(lesson.Date.TimeSlot);
        var start = interval.Start.ToString("HH:mm");
        var end = interval.End.ToString("HH:mm");
        var courseName = schedule.Get(lesson.Lesson.Course).FullName;
        var teachers = lesson.Lesson.Teachers
            .Select(x => schedule.Get(x).PersonName.ToString())
            .ToArray();
        return new LessonResult(
            Day: lesson.Date.DayOfWeek.ToString(),
            Parity: lesson.Date.Parity.ToString(),
            Slot: lesson.Date.TimeSlot.Index + 1,
            Start: start,
            End: end,
            Room: lesson.Lesson.Room.Id,
            Course: courseName,
            Teachers: teachers);
    }
}

public sealed class UnknownRoomException(string room) : ArgumentException($"Unknown room: {room}")
{
    public string Room { get; } = room;
}

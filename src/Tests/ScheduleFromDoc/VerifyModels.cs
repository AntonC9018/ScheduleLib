using System.Globalization;
using System.Text;
using ScheduleLib;

namespace ScheduleFromDoc.Tests;

public sealed class ScheduleVerifyModel
{
    public required RegularLessonVM[] RegularLessons { get; init; }
}

public sealed class RegularLessonVM
{
    public required string Day { get; init; }
    public required int Time { get; init; }
    public required string Parity { get; init; }
    public required string Period { get; init; }
    public required string Course { get; init; }
    public required string Room { get; init; }
    public required string Type { get; init; }
    public required string[] Groups { get; init; }
    public required string[] Teachers { get; init; }
    public string? SubGroup { get; init; }
}

public static class VerifyModelMapper
{
    public static ScheduleVerifyModel ToVerifyModel(Schedule schedule)
    {
        var regular = schedule.RegularLessons
            .Select(x => MapRegular(schedule, x))
            .OrderBy(x => x.Day)
            .ThenBy(x => x.Time)
            .ThenBy(x => x.Course)
            .ToArray();

        return new()
        {
            RegularLessons = regular,
        };
    }

    private static RegularLessonVM MapRegular(Schedule s, RegularLesson x)
    {
        var lesson = x.Lesson;
        var date = x.Date;

        var period = !date.Period.IsSpecified ? "" : FormatPeriod(s.Get(date.Period));
        var room = !lesson.Room.IsValid ? "" : s.Get(lesson.Room);

        return new()
        {
            Day = date.DayOfWeek.ToString(),
            Time = date.TimeSlot.Index,
            Parity = date.Parity.ToString(),
            Period = period,
            Course = s.Get(lesson.Course).FullName,
            Room = room,
            Type = lesson.Type.ToString(),
            Groups = EnumerateGroups(s, lesson.Groups),
            Teachers = lesson.Teachers.Select(tid =>
            {
                var teach = s.Get(tid);
                var sb = new StringBuilder();
                NameDisplayHelper.Append(new()
                {
                    Name = teach.PersonName,
                    Output = sb,
                    LastNameFirst = false,
                    InsertSpaceAfterShortName = true,
                    PreferLonger = true,
                });
                return sb.ToString();
            }).ToArray(),
            SubGroup = lesson.SubGroup.Value,
        };
    }

    private static string[] EnumerateGroups(Schedule s, LessonGroups groups)
    {
        var ret = new string[groups.Count];
        for (int index = 0; index < groups.Count; index++)
        {
            var gid = groups[index];
            ret[index] = s.Get(gid).Name;
        }
        return ret;
    }

    private static string FormatPeriod(Period p)
    {
        var sb = new StringBuilder();
        var listBuilder = new ListStringBuilder(sb, " to ");
        listBuilder.Append($"{p.Start:dd-MM-yy}");
        if (p.End is { } end)
        {
            listBuilder.Append($"{end:dd-MM-yy}");
        }
        return sb.ToString();
    }
}



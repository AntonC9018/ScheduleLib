using System.Collections.Immutable;
using System.Text;
using ScheduleLib;
using ScheduleLib.Generation;

namespace MainCli.JsonWebsite;

public sealed class RootObject
{
    public required ImmutableArray<ScheduleDaysDto> ScheduleDaysDto { get; set; }
}

public sealed class ScheduleDaysDto
{
    public required int ScheduleDaysId { get; set; }
    public required ImmutableArray<SchedulePairsDto> SchedulePairsDto { get; set; }
    public required string Weekday { get; set; }
}

public sealed class SchedulePairsDto
{
    public required int SchedulePairsId { get; set; }
    public required string PairInfo { get; set; }
    public required string PairTime { get; set; }
    public required string WeekType { get; set; }
}

public static class WebsiteJsonScheduleHelper
{
    public static RootObject CreateSerializationModel(
        FilteredSchedule schedule)
    {
        var ret = ImmutableArray.CreateBuilder<ScheduleDaysDto>();
        var groupedLessons = schedule.Lessons
            .GroupBy(x => x.Date.DayOfWeek);
        foreach (var dayGroup in groupedLessons)
        {
            var daysBuilder = ImmutableArray.CreateBuilder<SchedulePairsDto>();

            var timeSlotGroups = dayGroup
                .OrderBy(x => x.Date.TimeSlot);
            foreach (var lesson in timeSlotGroups)
            {
                var timeSlot = lesson.Date.TimeSlot;
                var romanTimeSlot = NumberHelper.ToRoman(timeSlot.Index + 1);
                var weekType = lesson.Date.Parity switch
                {
                    Parity.EvenWeek => "PAR",
                    Parity.EveryWeek => "GENERAL",
                    Parity.OddWeek => "IMPAR",
                    _ => throw Unreachable(),
                };

                var sb = new StringBuilder();
                var listBuilder = new ListStringBuilder(sb, ", ");

                {
                    var course = schedule.Source.Get(lesson.Lesson.Course);
                    listBuilder.Append(course.FullName);
                }
                if ()
                {

                }

            }

            var day = dayGroup.Key;
            var scheduleDayId = MondayBasedIndex(day);
            var weekdayLabel = day.ToString().ToUpper();
            var daysDto = new ScheduleDaysDto
            {
                Weekday = weekdayLabel,
                ScheduleDaysId = scheduleDayId,
                SchedulePairsDto =
            };

        }
        return new()
        {
            ScheduleDaysDto = ret.DrainToImmutable(),
        };
    }

    private static int MondayBasedIndex(DayOfWeek day)
    {
        const int weekDayCount = 7;
        return (day - DayOfWeek.Monday + weekDayCount) % weekDayCount;
    }
}


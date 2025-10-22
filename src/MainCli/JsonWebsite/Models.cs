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
    public struct Services
    {
        public required LessonTypeDisplayHandler LessonTypeDisplay;
        public required ParityDisplayHandler ParityDisplay;
        public required SubGroupNumberDisplayHandler SubGroupNumberDisplay;
    }

    public static RootObject CreateSerializationModel(
        FilteredSchedule schedule,
        Services services)
    {
        var ret = ImmutableArray.CreateBuilder<ScheduleDaysDto>();
        var groupedLessons = schedule.Lessons
            .GroupBy(x => x.Date.DayOfWeek)
            .OrderBy(g => MondayBasedIndex(g.Key));

        int pairIdCounter = 1;

        foreach (var dayGroup in groupedLessons)
        {
            var daysBuilder = ImmutableArray.CreateBuilder<SchedulePairsDto>();
            var timeSlotGroups = dayGroup
                .GroupBy(x => x.Date.TimeSlot)
                .OrderBy(g => g.Key.Index);

            foreach (var timeSlotGroup in timeSlotGroups)
            {
                var timeSlot = timeSlotGroup.Key;
                var romanTimeSlot = NumberHelper.ToRoman(timeSlot.Index + 1);

                // Group lessons by similar characteristics to potentially merge them
                foreach (var lesson in timeSlotGroup)
                {
                    var weekType = lesson.Date.Parity switch
                    {
                        Parity.EvenWeek => "PAR",
                        Parity.EveryWeek => "GENERAL",
                        Parity.OddWeek => "IMPAR",
                        _ => throw Unreachable(),
                    };

                    var pairInfo = BuildPairInfo(schedule.Source, lesson.Item, services);

                    var pairDto = new SchedulePairsDto
                    {
                        SchedulePairsId = pairIdCounter++,
                        PairInfo = pairInfo,
                        PairTime = romanTimeSlot,
                        WeekType = weekType,
                    };

                    daysBuilder.Add(pairDto);
                }
            }

            var day = dayGroup.Key;
            var scheduleDayId = MondayBasedIndex(day) + 1; // +1 to match the example IDs
            var weekdayLabel = day.ToString().ToUpper();

            var daysDto = new ScheduleDaysDto
            {
                Weekday = weekdayLabel,
                ScheduleDaysId = scheduleDayId,
                SchedulePairsDto = daysBuilder.DrainToImmutable(),
            };

            ret.Add(daysDto);
        }

        return new() { ScheduleDaysDto = ret.ToImmutable(), };
    }

    private static string BuildPairInfo(
        Schedule schedule,
        RegularLesson lesson,
        Services services)
    {
        var sb = new StringBuilder();

        // Course name
        var course = schedule.Get(lesson.Lesson.Course);
        sb.Append(course.FullName);
        var listBuilder = new ListStringBuilder(sb);

        {
            ListStringBuilder detailListBuilder = default;
            bool isFirstDetail = true;

            void MaybeStartDetails()
            {
                if (!isFirstDetail)
                {
                    return;
                }

                sb.Append('(');
                detailListBuilder = new(sb);
                isFirstDetail = true;
            }

            void EndDetails()
            {
                if (isFirstDetail)
                {
                    return;
                }

                sb.Append(')');
            }

            var lessonType = services.LessonTypeDisplay.Get(lesson.Lesson.Type);
            if (lessonType != null)
            {
                MaybeStartDetails();
                detailListBuilder.Append($"{lessonType}");
            }

            var parity = services.ParityDisplay.Get(lesson.Date.Parity);
            if (parity != null)
            {
                MaybeStartDetails();
                detailListBuilder.Append($"{parity}");
            }

            EndDetails();
        }

        // Groups
        foreach (var groupId in lesson.Lesson.Groups)
        {
            var group = schedule.Get(groupId);
            listBuilder.Append(group.Name);
        }

        // Subgroup
        var subGroupNumber = services.SubGroupNumberDisplay.Get(lesson.Lesson.SubGroup);
        if (subGroupNumber != null)
        {
            listBuilder.Append($"s.{subGroupNumber}");
        }

        // Room
        if (lesson.Lesson.Room.IsValid)
        {
            var room = schedule.Get(lesson.Lesson.Room);
            listBuilder.Append(room);
        }

        return sb.ToString();
    }

    private static int MondayBasedIndex(DayOfWeek day)
    {
        const int weekDayCount = 7;
        return ((int) day - (int) DayOfWeek.Monday + weekDayCount) % weekDayCount;
    }
}

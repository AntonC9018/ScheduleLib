using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using ScheduleLib;
using ScheduleLib.Generation;

namespace WebsiteJsonSchedule;

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

// Most of this code is AI generated.
public static class WebsiteJsonScheduleHelper
{
    public struct Services
    {
        public required LessonTypeDisplayHandler LessonTypeDisplay;
        public required ParityDisplayHandler ParityDisplay;
        public required SubGroupNumberDisplayHandler SubGroupNumberDisplay;
    }

    // Key for grouping lessons that can be merged
    private readonly record struct LessonGroupKey(
        CourseId Course,
        LessonType Type,
        RoomId Room);

    private static readonly JsonSerializerOptions _jsonSettings = new()
    {
        WriteIndented = true,
    };

    public static async Task Serialize(RootObject obj, Stream outputStream)
    {
        await JsonSerializer.SerializeAsync(
            outputStream,
            obj,
            _jsonSettings);
    }

    public static RootObject CreateSerializationModel(
        FilteredSchedule schedule,
        Services services)
    {
        var ret = ImmutableArray.CreateBuilder<ScheduleDaysDto>();
        var groupedLessons = schedule.Lessons
            .GroupBy(x => x.Date.DayOfWeek)
            .OrderBy(x => x.Key);

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

                // Group lessons by course, type, and room
                var lessonGroups = timeSlotGroup
                    .GroupBy(lesson =>
                    {
                        var r = lesson.Ref;
                        return new LessonGroupKey(
                            r.Lesson.Course,
                            r.Lesson.Type,
                            r.Lesson.Room);
                    })
                    .ToList();

                foreach (var lessonGroup in lessonGroups)
                {
                    var lessons = lessonGroup.ToList();

                    // Determine the overall week type for this group
                    var weekType = DetermineWeekType(lessons);

                    var pairInfo = BuildPairInfo(schedule.Source, lessons, services);

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
            var scheduleDayId = MondayBasedIndex(day) + 1;
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

    private static string DetermineWeekType(List<WeeklyLessonAccessor> lessons)
    {
        // If all lessons have the same parity, use that
        var firstParity = lessons[0].Date.Parity;
        if (lessons.All(l => l.Date.Parity == firstParity))
        {
            return firstParity switch
            {
                Parity.EvenWeek => "PAR",
                Parity.EveryWeek => "GENERAL",
                Parity.OddWeek => "IMPAR",
                _ => throw Unreachable(),
            };
        }

        // Mixed parities default to GENERAL
        return "GENERAL";
    }

    private static string BuildPairInfo(
        Schedule schedule,
        List<WeeklyLessonAccessor> lessons,
        Services services)
    {
        var sb = new StringBuilder();
        var firstLesson = lessons[0];
        var listBuilder = new ListStringBuilder(sb, ", ");

        // Course name
        var course = schedule.Get(firstLesson.Lesson.Course);
        listBuilder.Append(course.FullName);

        var hasMixedParity = lessons.Select(l => l.Date.Parity).Distinct().Count() > 1;

        // Lesson type and details in parentheses
        {
            ListStringBuilder detailListBuilder = default;
            bool hasDetails = false;

            void MaybeStartDetails()
            {
                if (hasDetails)
                {
                    return;
                }
                hasDetails = true;
                sb.Append(" (");
                detailListBuilder = new(sb, ", ");
            }

            void EndDetails()
            {
                if (!hasDetails)
                {
                    return;
                }
                sb.Append(')');
            }

            var lessonType = services.LessonTypeDisplay.Get(firstLesson.Lesson.Type);
            if (lessonType != null)
            {
                MaybeStartDetails();
                detailListBuilder.Append(lessonType);
            }

            // Check if we need to add merged parity info
            if (!hasMixedParity)
            {
                var parity = services.ParityDisplay.Get(firstLesson.Date.Parity);
                if (parity != null)
                {
                    MaybeStartDetails();
                    detailListBuilder.Append(parity);
                }
            }

            EndDetails();
        }
        listBuilder.MaybeAppendSeparator();

        // Groups - handle merged groups with parity suffixes
        var groupsWithParity = new Dictionary<GroupId, List<(Parity parity, SubGroup subGroup)>>();
        foreach (var lesson in lessons)
        {
            foreach (var groupId in lesson.Lesson.Groups)
            {
                if (!groupsWithParity.ContainsKey(groupId))
                {
                    groupsWithParity[groupId] = new();
                }
                groupsWithParity[groupId].Add((lesson.Date.Parity, lesson.Lesson.SubGroup));
            }
        }

        foreach (var (groupId, parityList) in groupsWithParity)
        {
            var group = schedule.Get(groupId);
            var groupSb = new StringBuilder();
            groupSb.Append(group.Name);

            // Add parity suffix if this is a merged lesson with different parities
            if (hasMixedParity)
            {
                var distinctParities = parityList.Select(p => p.parity).Distinct().ToList();
                if (distinctParities.Count == 1 && distinctParities[0] != Parity.EveryWeek)
                {
                    var paritySuffix = services.ParityDisplay.Get(distinctParities[0]);
                    if (paritySuffix != null)
                    {
                        groupSb.Append('-');
                        groupSb.Append(paritySuffix);
                    }
                }
            }

            listBuilder.Append(groupSb.ToString());
        }

        // Subgroup - only if all lessons share the same subgroup
        var distinctSubGroups = lessons.Select(l => l.Lesson.SubGroup).Distinct().ToList();
        if (distinctSubGroups.Count == 1 && distinctSubGroups[0] != SubGroup.All)
        {
            var subGroupNumber = services.SubGroupNumberDisplay.Get(distinctSubGroups[0]);
            if (subGroupNumber != null)
            {
                listBuilder.Append($"s.{subGroupNumber}");
            }
        }

        // Room
        if (firstLesson.Lesson.Room.IsValid)
        {
            var room = schedule.Get(firstLesson.Lesson.Room);
            listBuilder.Append(room);
        }

        return sb.ToString();
    }

    private static int MondayBasedIndex(DayOfWeek day)
    {
        const int weekDayCount = 7;
        return ((int)day - (int)DayOfWeek.Monday + weekDayCount) % weekDayCount;
    }
}

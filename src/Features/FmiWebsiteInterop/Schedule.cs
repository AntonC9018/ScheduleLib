using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using FmiWebsiteInterop.Api;
using ScheduleLib;
using ScheduleLib.Generation;
using ScheduleLib.Helper;

namespace FmiWebsiteInterop.Schedule;

public sealed class RootObject
{
    public required ImmutableArray<ScheduleDaysDto> ScheduleDaysDto { get; set; }
}

public sealed class ScheduleDaysDto
{
    public required ImmutableArray<SchedulePairsDto> SchedulePairsDto { get; set; }
    public required string Weekday { get; set; }
}

public sealed class SchedulePairsDto
{
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

    public static async Task Serialize(RootObject obj, Stream outputStream)
    {
        await JsonSerializer.SerializeAsync(
            outputStream,
            obj,
            options: ItUsmWebsiteApi.JsonOptions);
    }

    public static RootObject CreateSerializationModel(
        FilteredSchedule schedule,
        Services services)
    {
        var ret = ImmutableArray.CreateBuilder<ScheduleDaysDto>();
        var groupedLessons = schedule.EnumerateWeeklyLessons()
            .GroupBy(x => x.Date.DayOfWeek)
            .OrderBy(x => x.Key);

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

                // Build per-lesson-group DTOs first, then merge those sharing (PairTime, WeekType)
                // so that two different courses in the same room/slot produce one concatenated entry.
                var slotDtos = new List<SchedulePairsDto>();
                foreach (var lessonGroup in lessonGroups)
                {
                    var lessons = lessonGroup.ToList();
                    var weekType = DetermineWeekType(lessons);
                    var pairInfo = BuildPairInfo(schedule.Source, lessons, services);

                    slotDtos.Add(new SchedulePairsDto
                    {
                        PairInfo = pairInfo,
                        PairTime = romanTimeSlot,
                        WeekType = weekType,
                    });
                }

                // Merge entries that share the same time slot and week type
                foreach (var mergeGroup in slotDtos.GroupBy(d => (d.PairTime, d.WeekType)))
                {
                    daysBuilder.Add(new SchedulePairsDto
                    {
                        PairInfo = string.Join("\n", mergeGroup.Select(d => d.PairInfo)),
                        PairTime = mergeGroup.Key.PairTime,
                        WeekType = mergeGroup.Key.WeekType,
                    });
                }
            }

            var day = dayGroup.Key;
            var weekdayLabel = day.ToString().ToUpper();

            var daysDto = new ScheduleDaysDto
            {
                Weekday = weekdayLabel,
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
        ScheduleLib.Schedule schedule,
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

        // Split - only if all lessons share the same subgroup and specialization
        var distinctSplits = lessons.Select(l => l.Lesson.GroupSplitKey).Distinct().ToList();
        if (distinctSplits.Count == 1 && distinctSplits[0].ToDisplayString() is { } splitDisplay)
        {
            listBuilder.Append($"s.{splitDisplay}");
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

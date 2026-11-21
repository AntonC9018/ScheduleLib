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

public static class WebsiteJsonScheduleHelper
{
    public struct Services
    {
        public required LessonTypeDisplayHandler LessonTypeDisplay;
        public required ParityDisplayHandler ParityDisplay;
    }

    private readonly record struct LessonGroupKey(
        CourseId Course,
        LessonType Type,
        RoomId Room);

    public static async Task Serialize(
        RootObject obj,
        Stream outputStream,
        CancellationToken cancellationToken = default)
    {
        await JsonSerializer.SerializeAsync(
            outputStream,
            obj,
            options: ItUsmWebsiteApi.JsonOptions,
            cancellationToken: cancellationToken);
    }

    public static RootObject CreateSerializationModel(
        FilteredSchedule schedule,
        Services services)
    {
        var days = ImmutableArray.CreateBuilder<ScheduleDaysDto>();
        var groupedByDay = schedule.EnumerateWeeklyLessons()
            .GroupBy(x => x.Date.DayOfWeek)
            .OrderBy(x => x.Key);

        foreach (var dayGroup in groupedByDay)
        {
            var pairs = BuildDayPairs(schedule.Source, dayGroup, services);
            days.Add(new ScheduleDaysDto
            {
                Weekday = dayGroup.Key.ToString().ToUpperInvariant(),
                SchedulePairsDto = pairs,
            });
        }

        return new()
        {
            ScheduleDaysDto = days.ToImmutable(),
        };
    }

    private static ImmutableArray<SchedulePairsDto> BuildDayPairs(
        ScheduleLib.Schedule source,
        IEnumerable<WeeklyLessonAccessor> dayLessons,
        Services services)
    {
        var pairs = ImmutableArray.CreateBuilder<SchedulePairsDto>();
        var timeSlotGroups = dayLessons
            .GroupBy(x => x.Date.TimeSlot)
            .OrderBy(g => g.Key.Index);

        foreach (var timeSlotGroup in timeSlotGroups)
        {
            var romanTimeSlot = NumberHelper.ToRoman(timeSlotGroup.Key.Index + 1);
            var lessonGroups = timeSlotGroup.GroupBy(lesson =>
            {
                var r = lesson.Ref;
                return new LessonGroupKey(
                    r.Lesson.Course,
                    r.Lesson.Type,
                    r.Lesson.Room);
            });

            // Different courses in the same slot with the same week type must end up as one
            // concatenated entry, so merge only after building per-group DTOs.
            var slotDtos = new List<SchedulePairsDto>();
            foreach (var lessonGroup in lessonGroups)
            {
                var lessons = lessonGroup.ToList();
                var uniformParity = GetUniformParity(lessons);
                var weekType = FormatWeekType(uniformParity);
                var pairInfo = BuildPairInfo(
                    source,
                    lessons,
                    uniformParity,
                    services);

                slotDtos.Add(new SchedulePairsDto
                {
                    PairInfo = pairInfo,
                    PairTime = romanTimeSlot,
                    WeekType = weekType,
                });
            }

            foreach (var mergeGroup in slotDtos.GroupBy(d => d.WeekType))
            {
                pairs.Add(new SchedulePairsDto
                {
                    PairInfo = string.Join("\n", mergeGroup.Select(d => d.PairInfo)),
                    PairTime = romanTimeSlot,
                    WeekType = mergeGroup.Key,
                });
            }
        }

        return pairs.DrainToImmutable();
    }

    private static Parity? GetUniformParity(List<WeeklyLessonAccessor> lessons)
    {
        var first = lessons[0].Date.Parity;
        for (int i = 1; i < lessons.Count; i++)
        {
            if (lessons[i].Date.Parity != first)
            {
                return null;
            }
        }
        return first;
    }

    private static string FormatWeekType(Parity? uniformParity)
    {
        if (uniformParity is null)
        {
            return "GENERAL";
        }

        return uniformParity.Value switch
        {
            Parity.EvenWeek => "PAR",
            Parity.EveryWeek => "GENERAL",
            Parity.OddWeek => "IMPAR",
            _ => throw Unreachable(),
        };
    }

    private static string BuildPairInfo(
        ScheduleLib.Schedule schedule,
        List<WeeklyLessonAccessor> lessons,
        Parity? uniformParity,
        Services services)
    {
        var sb = new StringBuilder();
        var firstLesson = lessons[0];
        var listBuilder = new ListStringBuilder(sb, ", ");

        var course = schedule.Get(firstLesson.Lesson.Course);
        listBuilder.Append(course.FullName);

        AppendDetails(sb, firstLesson, uniformParity, services);
        AppendGroups(sb, listBuilder, schedule, lessons, uniformParity is null, services);

        // Lessons are grouped by course/type/room, so mixed partitions are possible;
        // when they differ, no single suffix applies and none is shown.
        var firstPartition = firstLesson.Lesson.GroupPartitionKey;
        if (HasUniformPartition(lessons, firstPartition)
            && firstPartition.ToDisplayString() is { } partitionDisplay)
        {
            listBuilder.Append($"s.{partitionDisplay}");
        }

        if (firstLesson.Lesson.Room.IsValid)
        {
            var room = schedule.Get(firstLesson.Lesson.Room);
            listBuilder.Append(room);
        }

        return sb.ToString();
    }

    private static void AppendDetails(
        StringBuilder sb,
        WeeklyLessonAccessor firstLesson,
        Parity? uniformParity,
        Services services)
    {
        var lessonType = services.LessonTypeDisplay.Get(firstLesson.Lesson.Type);
        string? parity = null;
        if (uniformParity is { } p)
        {
            parity = services.ParityDisplay.Get(p);
        }

        if (lessonType is null && parity is null)
        {
            return;
        }

        sb.Append(" (");
        var details = new ListStringBuilder(sb, ", ");
        if (lessonType is not null)
        {
            details.Append(lessonType);
        }
        if (parity is not null)
        {
            details.Append(parity);
        }
        sb.Append(')');
    }

    private static void AppendGroups(
        StringBuilder sb,
        ListStringBuilder listBuilder,
        ScheduleLib.Schedule schedule,
        List<WeeklyLessonAccessor> lessons,
        bool hasMixedParity,
        Services services)
    {
        // Track each group's parity while preserving insertion order; null marks mixed parities.
        var groupParities = new Dictionary<GroupId, Parity?>();
        foreach (var lesson in lessons)
        {
            var lessonParity = lesson.Date.Parity;
            foreach (var groupId in lesson.Lesson.Groups)
            {
                if (!groupParities.TryGetValue(groupId, out var existing))
                {
                    groupParities.Add(groupId, lessonParity);
                }
                else if (existing != lessonParity)
                {
                    groupParities[groupId] = null;
                }
            }
        }

        foreach (var (groupId, groupParity) in groupParities)
        {
            var group = schedule.Get(groupId);
            listBuilder.Append(group.Name);

            if (!hasMixedParity)
            {
                continue;
            }
            if (groupParity is not { } parity || parity == Parity.EveryWeek)
            {
                continue;
            }
            if (services.ParityDisplay.Get(parity) is not { } paritySuffix)
            {
                continue;
            }

            sb.Append('-');
            sb.Append(paritySuffix);
        }
    }

    private static bool HasUniformPartition(
        List<WeeklyLessonAccessor> lessons,
        GroupPartitionKey expected)
    {
        for (int i = 1; i < lessons.Count; i++)
        {
            if (!lessons[i].Lesson.GroupPartitionKey.Equals(expected))
            {
                return false;
            }
        }
        return true;
    }
}

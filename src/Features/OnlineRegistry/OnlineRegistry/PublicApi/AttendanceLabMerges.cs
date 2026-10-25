using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib.OnlineRegistry;

/// <summary>Projects canonical subgroup attendance onto confirmed whole-group labs.</summary>
public static class AttendanceLabMerges
{
    internal static bool TryDestination(Schedule schedule, IReadOnlyList<RegistryLabMergeRule> rules,
        StudentsLookupKey source, out GroupPartitionKey destination)
    {
        destination = source.GroupPartition with { SubGroup = SubGroup.All };
        bool CheckCondition()
        {
            if (source.LessonType != LessonType.Lab)
            {
                return true;
            }

            if (!source.Groups.IsSingleGroup)
            {
                return true;
            }

            if (source.GroupPartition.SubGroup.Value is not { } subgroup)
            {
                return true;
            }

            if (NumberHelper.FromRoman(subgroup) is null)
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            return false;
        }

        var group = source.Groups[0];
        var partition = source.GroupPartition;
        var automatic = schedule.AutomaticLabSubGroupMerges.Any(merge =>
        {
            if (merge.Course != source.CourseId)
            {
                return false;
            }
            if (merge.Group != group)
            {
                return false;
            }
            if (merge.Specialization != partition.Specialization)
            {
                return false;
            }
            if (merge.Alternative != partition.Alternative)
            {
                return false;
            }
            return true;
        });
        if (!automatic && RegistryLabMerges.Find(rules, schedule, source.CourseId, source.Groups, destination) is null)
        {
            return false;
        }

        // Do not invent a source partition merely because a merge exists.
        if (schedule.WeeklyLessons.Any(item => item.Base.Data.Course == source.CourseId
           && item.Base.Data.Groups.IsSingleGroup && item.Base.Data.Groups[0] == group
           && item.Base.Data.Type == LessonType.Lab && item.Base.Data.GroupPartitionKey == partition)
           || schedule.AutomaticLabSubGroupMerges.Any(merge => merge.Course == source.CourseId
               && merge.Group == group && merge.Specialization == partition.Specialization
               && merge.Alternative == partition.Alternative && merge.SurvivingSubGroup == partition.SubGroup))
        {
            return true;
        }

        return false;
    }

    public static StudentAttendanceList Project(StudentAttendanceList attendance, Schedule schedule,
        IReadOnlyList<RegistryLabMergeRule> rules)
    {
        var result = attendance.ToDictionary();
        var sources = new Dictionary<StudentsLookupKey, List<StudentAttendanceList.BetterAttendances>>();
        foreach (var entry in attendance.OrderBy(entry => entry.Key.GroupPartition.SubGroup.Value, StringComparer.Ordinal))
        {
            if (!TryDestination(schedule, rules, entry.Key, out var partition))
            {
                continue;
            }

            var key = entry.Key with { GroupPartition = partition };
            if (result.ContainsKey(key))
            {
                continue; // An explicitly supplied whole-group sheet takes precedence.
            }

            if (!sources.TryGetValue(key, out var lists))
            {
                global::System.Collections.Generic.Dictionary<global::ScheduleLib.OnlineRegistry.StudentsLookupKey, global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances>> dictionary = sources;
                global::ScheduleLib.OnlineRegistry.StudentsLookupKey key2 = key;
                global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances> value = lists = [];
                dictionary.Add(key2, value);
            }

            lists.Add(entry.Value);
        }
        foreach (var entry in sources)
        {
            global::System.Collections.Generic.Dictionary<global::ScheduleLib.OnlineRegistry.StudentsLookupKey, global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances> dictionary2 = result;
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key3 = entry.Key;
            global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances value2 = Combine(entry.Value);
            dictionary2.Add(key3, value2);
        }

        return new(result);
    }

    private static StudentAttendanceList.BetterAttendances Combine(
        IReadOnlyList<StudentAttendanceList.BetterAttendances> sources)
    {
        var dated = sources[0].Dates is not null;
        if (sources.Any(source => (source.Dates is not null) != dated))
        {
            throw new InvalidOperationException("Merged lab attendance cannot mix dated and ordinal sources.");
        }

        var names = NamesInDb.Create([.. sources.SelectMany(source => source.StudentNames
            .OrderBy(student => student.Value).Select(student => student.Key))]);
        global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.OnlineRegistry.AttendanceDate>? nullable;
        if (dated)
        {
            nullable = [.. sources.SelectMany(source => source.Dates!.Value).Distinct()
                .OrderBy(date => date.Year).ThenBy(date => date.Month).ThenBy(date => date.Day).ThenBy(date => date.Time)];
        }
        else
        {
            nullable = null;
        }

        ImmutableArray<AttendanceDate>? dates = nullable;
        var dayCount = dates?.Length ?? sources.Max(source => source.Attendance.DayCount);
        var days = ImmutableArray.CreateBuilder<DayAttendanceList>(dayCount);
        for (int index = 0; index < dayCount; index++)
        {
            var marks = ImmutableArray.CreateBuilder<Attendance>(names.Count);
            foreach (var source in sources)
            {
                var day = index;
                if (dates is { } targetDates)
                {
                    var matches = source.Dates!.Value.Select((date, position) => (Date: date, Position: position))
                        .Where(column => column.Date == targetDates[index]).ToArray();
                    day = matches.Length == 1 ? matches[0].Position : -1;
                }
                if (day >= 0 && day < source.Attendance.DayCount)
                {
                    marks.AddRange(source.Attendance.Day(day).AsArray());
                }
                else
                {
                    marks.AddRange(Enumerable.Repeat(Attendance.None, source.StudentNames.Count));
                }
            }
            global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.OnlineRegistry.DayAttendanceList>.Builder builder = days;
            global::ScheduleLib.OnlineRegistry.DayAttendanceList item = new(marks.MoveToImmutable());
            builder.Add(item);
        }
        global::ScheduleLib.OnlineRegistry.AttendanceLists attendance = new(days.MoveToImmutable());
        global::ScheduleLib.OnlineRegistry.NamesInDb studentNames = names;
        global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.OnlineRegistry.AttendanceDate>? dates2 = dates;
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances betterAttendances = new(attendance, studentNames, dates2);
        return betterAttendances;
    }
}

using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib.OnlineRegistry;

/// <summary>Projects canonical subgroup attendance onto confirmed whole-group labs.</summary>
public static class AttendanceLabMerges
{
    /// <summary>One dated attendance column of a source list.</summary>
    private readonly record struct AttendanceDateColumn(AttendanceDate Date, int Position);

    internal static bool TryDestination(
        Schedule schedule,
        IReadOnlyList<RegistryLabMergeRule> rules,
        StudentsLookupKey source,
        out GroupPartitionKey destination)
    {
        destination = source.GroupPartition with
        {
            SubGroup = SubGroup.All,
        };

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

            if (!(merge.Alternative != partition.Alternative))
            {
                return true;
            }

            return false;
        });
        if (!automatic
            && RegistryLabMerges.Find(rules,
                schedule,
                source.CourseId,
                source.Groups,
                destination) is null)
        {
            return false;
        }

        // Do not invent a source partition merely because a merge exists.
        if (schedule.WeeklyLessons.Any(item =>
            {
                if (!(item.Base.Data.Course == source.CourseId))
                {
                    return false;
                }
                if (!item.Base.Data.Groups.IsSingleGroup)
                {
                    return false;
                }
                if (!(item.Base.Data.Groups[0] == group))
                {
                    return false;
                }
                if (item.Base.Data.Type != LessonType.Lab)
                {
                    return false;
                }
                if (item.Base.Data.GroupPartitionKey == partition)
                {
                    return true;
                }

                return false;
            })
            || schedule.AutomaticLabSubGroupMerges.Any(merge =>
            {
                if (!(merge.Course == source.CourseId))
                {
                    return false;
                }
                if (!(merge.Group == group))
                {
                    return false;
                }
                if (!(merge.Specialization == partition.Specialization))
                {
                    return false;
                }
                if (!(merge.Alternative == partition.Alternative))
                {
                    return false;
                }
                if (merge.SurvivingSubGroup == partition.SubGroup)
                {
                    return true;
                }

                return false;
            }))
        {
            return true;
        }

        return false;
    }

    public static StudentAttendanceList Project(
        StudentAttendanceList attendance,
        Schedule schedule,
        IReadOnlyList<RegistryLabMergeRule> rules)
    {
        var result = attendance.ToDictionary();
        var sources = new Dictionary<StudentsLookupKey, List<StudentAttendanceList.BetterAttendances>>();
        foreach (var entry in attendance.OrderBy(entry => entry.Key.GroupPartition.SubGroup.Value,
                     StringComparer.Ordinal))
        {
            if (!TryDestination(schedule,
                    rules,
                    entry.Key,
                    out var partition))
            {
                continue;
            }

            var key = entry.Key with
            {
                GroupPartition = partition,
            };
            if (result.ContainsKey(key))
            {
                continue; // An explicitly supplied whole-group sheet takes precedence.
            }

            if (!sources.TryGetValue(key, out var lists))
            {
                var dictionary = sources;
                var key2 = key;
                var value = lists = [];
                dictionary.Add(key2, value);
            }

            lists.Add(entry.Value);
        }
        foreach (var entry in sources)
        {
            var dictionary2 = result;
            var key3 = entry.Key;
            var value2 = Combine(entry.Value);
            dictionary2.Add(key3, value2);
        }

        return new(result);
    }

    private static StudentAttendanceList.BetterAttendances Combine(
        IReadOnlyList<StudentAttendanceList.BetterAttendances> sources)
    {
        var dated = sources[0].Dates is not null;
        if (sources.Any(source => source.Dates is not null != dated))
        {
            throw new InvalidOperationException("Merged lab attendance cannot mix dated and ordinal sources.");
        }

        var names = NamesInDb.Create([
            .. sources.SelectMany(source => source.StudentNames
                .OrderBy(student => student.Value)
                .Select(student => student.Key)),
        ]);
        ImmutableArray<AttendanceDate>? nullable;
        if (dated)
        {
            nullable =
            [
                .. sources.SelectMany(source => source.Dates!.Value)
                    .Distinct()
                    .OrderBy(date => date.Year)
                    .ThenBy(date => date.Month)
                    .ThenBy(date => date.Day)
                    .ThenBy(date => date.Time),
            ];
        }
        else
        {
            nullable = null;
        }

        var dates = nullable;
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
                    var matches = source.Dates!.Value
                        .Select((date, position) => new AttendanceDateColumn(date, position))
                        .Where(column => column.Date == targetDates[index])
                        .ToArray();
                    day = matches.Length == 1 ? matches[0].Position : -1;
                }
                if (day >= 0 && day < source.Attendance.DayCount)
                {
                    marks.AddRange(source.Attendance.Day(day)
                        .AsArray());
                }
                else
                {
                    marks.AddRange(Enumerable.Repeat(Attendance.None, source.StudentNames.Count));
                }
            }
            var builder = days;
            DayAttendanceList item = new(marks.MoveToImmutable());
            builder.Add(item);
        }
        AttendanceLists attendance = new(days.MoveToImmutable());
        var studentNames = names;
        var dates2 = dates;
        StudentAttendanceList.BetterAttendances betterAttendances = new(attendance,
            studentNames,
            dates2);
        return betterAttendances;
    }
}

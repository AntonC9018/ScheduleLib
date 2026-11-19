using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib;

public readonly record struct DetectedLabSubGroupMerge(
    CourseId Course,
    GroupId Group,
    Specialization Specialization,
    Alternative Alternative,
    SubGroup SurvivingSubGroup,
    PeriodId EvidencePeriod);

public readonly record struct LabSubGroupMergeDiagnostic(
    CourseId Course,
    GroupId Group,
    Specialization Specialization,
    Alternative Alternative,
    string Reason);

/// <summary>
/// Derives a semester-wide whole-group audience from a newer timetable replacing
/// numeric lab subgroups. Raw source lessons and their lookup IDs stay intact.
/// </summary>
public static class LabSubGroupMerges
{
    private readonly record struct Key(
        CourseId Course,
        GroupId Group,
        Specialization Specialization,
        Alternative Alternative);

    public static Schedule Project(Schedule source, bool enabled)
    {
        if (!enabled || !source.AutomaticLabSubGroupMerges.IsEmpty)
        {
            return source;
        }

        var merges = ImmutableArray.CreateBuilder<DetectedLabSubGroupMerge>();
        var diagnostics = ImmutableArray.CreateBuilder<LabSubGroupMergeDiagnostic>();
        var suppressed = ImmutableHashSet.CreateBuilder<WeeklyLessonId>();
        var lessons = source.WeeklyLessons.ToBuilder();
        var labs = source.EnumerateWeeklyLessons()
            .Where(x => x.Lesson.Type == LessonType.Lab)
            .ToArray();
        var candidates = labs.Where(x => x.Lesson.Groups.IsSingleGroup && !x.Lesson.Groups.IsEmpty)
            .GroupBy(x => new Key(
                x.Lesson.Course,
                x.Lesson.Group,
                x.Lesson.Specialization,
                x.Lesson.Alternative));

        foreach (var candidate in candidates)
        {
            var key = candidate.Key;
            var numeric = candidate.Where(x => IsNumeric(x.Lesson.SubGroup))
                .ToArray();
            if (numeric.Length == 0)
            {
                continue;
            }

            var applicable = source.EnumerateWeeklyLessons()
                .Where(x => x.Lesson.Groups.Contains(key.Group) && x.Date.Period.IsSpecified)
                .Select(x => x.Date.Period)
                .Distinct()
                .OrderBy(x => source.Get(x).Start)
                .ToArray();
            if (applicable.Length < 2)
            {
                Diagnose("At least two dated schedule periods are required.");
                continue;
            }

            static bool HasUndatedSubGroupEvidence(WeeklyLessonAccessor lab)
            {
                if (lab.Date.Period.IsSpecified)
                {
                    return false;
                }

                var subGroup = lab.Lesson.SubGroup;
                if (IsNumeric(subGroup) || subGroup == SubGroup.All)
                {
                    return true;
                }

                return false;
            }

            bool HasDiscontinuousPeriods()
            {
                return applicable.Zip(applicable.Skip(1))
                    .Any(pair =>
                    {
                        var previousPeriod = source.Get(pair.First);
                        var nextPeriod = source.Get(pair.Second);
                        if (previousPeriod.End != nextPeriod.Start)
                        {
                            return true;
                        }

                        if (previousPeriod.Start >= nextPeriod.Start)
                        {
                            return true;
                        }

                        return false;
                    });
            }

            if (candidate.Any(HasUndatedSubGroupEvidence) || HasDiscontinuousPeriods())
            {
                Diagnose("Period evidence is undated, overlapping, or incomplete.");
                continue;
            }

            bool IsSharedLabInPartition(WeeklyLessonAccessor lab)
            {
                if (lab.Lesson.Groups.IsSingleGroup)
                {
                    return false;
                }

                if (!lab.Lesson.Groups.Contains(key.Group))
                {
                    return false;
                }

                if (lab.Lesson.Course == key.Course
                    && lab.Lesson.Specialization == key.Specialization
                    && lab.Lesson.Alternative == key.Alternative)
                {
                    return true;
                }

                return false;
            }

            if (labs.Any(IsSharedLabInPartition))
            {
                Diagnose("Shared lab lessons require separate evidence for each parent group.");
                continue;
            }

            var latest = applicable[^1];
            var current = candidate.Where(x => x.Date.Period == latest)
                .ToArray();
            var currentNumeric = current.Where(x => IsNumeric(x.Lesson.SubGroup))
                .ToArray();
            var labels = currentNumeric.Select(x => x.Lesson.SubGroup)
                .Distinct()
                .ToArray();
            var whole = current.Where(x => x.Lesson.SubGroup == SubGroup.All)
                .ToArray();
            if (labels.Length > 1)
            {
                var previousLabels = numeric.Where(x => x.Date.Period != latest)
                    .Select(x => x.Lesson.SubGroup)
                    .Distinct()
                    .ToArray();
                if (previousLabels.Except(labels)
                    .Any())
                {
                    Diagnose("More than one numeric subgroup survives.");
                }

                continue;
            }

            if (labels.Length == 0 && whole.Length == 0)
            {
                Diagnose("The latest group period has no replacement labs for this course partition.");
                continue;
            }

            if (labels.Length == 1 && whole.Length != 0)
            {
                Diagnose("The latest period mixes numeric and whole-group labs.");
                continue;
            }

            // A repeated single-subgroup timetable still carries the earlier merge.
            // Stop at the nearest split, checking intervening periods rather than
            // inferring from an arbitrary union of historical subgroup labels.
            var before = Array.Empty<WeeklyLessonAccessor>();
            var valid = true;
            foreach (var previous in applicable.Reverse()
                         .Skip(1))
            {
                var previousLabs = candidate.Where(x =>
                    {
                        if (x.Date.Period != previous)
                        {
                            return false;
                        }

                        if (IsNumeric(x.Lesson.SubGroup) || x.Lesson.SubGroup == SubGroup.All)
                        {
                            return true;
                        }

                        return false;
                    })
                    .ToArray();
                var previousLabels = previousLabs.Where(x => IsNumeric(x.Lesson.SubGroup))
                    .Select(x => x.Lesson.SubGroup)
                    .Distinct()
                    .ToArray();
                if (previousLabels.Length >= 2)
                {
                    before = previousLabs;
                    break;
                }

                bool BreaksSubgroupContinuity()
                {
                    if (previousLabs.Length == 0)
                    {
                        return true;
                    }
                    if (labels.Length != 1)
                    {
                        return false;
                    }
                    if (previousLabels.Length != 1)
                    {
                        return false;
                    }

                    if (previousLabels[0] != labels[0])
                    {
                        return true;
                    }

                    return false;
                }

                if (BreaksSubgroupContinuity())
                {
                    valid = false;
                    break;
                }
            }

            if (!valid || before.Length == 0)
            {
                Diagnose("No continuous earlier split-lab timetable proves a merge.");
                continue;
            }
            if (labels.Length == 1 && !before.Any(x => x.Lesson.SubGroup == labels[0]))
            {
                Diagnose("The latest numeric subgroup is absent from the earlier split timetable.");
                continue;
            }
            var retained = labels.Length == 1 ? currentNumeric : whole;
            var survivor = labels.Length == 1 ? labels[0] : SubGroup.All;
            merges.Add(new(
                key.Course,
                key.Group,
                key.Specialization,
                key.Alternative,
                survivor,
                latest));
            foreach (var old in candidate.Where(x => IsNumeric(x.Lesson.SubGroup) || x.Lesson.SubGroup == SubGroup.All))
            {
                suppressed.Add(old.Id);
            }

            foreach (var item in retained)
            {
                suppressed.Remove(item.Id);
                var lesson = lessons[item.Id.Id];
                var data = lesson.Base.Data;
                var mergedData = data.WithGroupsAndSubGroup(data.Groups, SubGroup.All);
                var date = lesson.Date;
                var mergedDate = new WeeklyLessonDate(
                    dayOfWeek: date.DayOfWeek,
                    timeSlot: date.TimeSlot,
                    period: PeriodId.Unspecified,
                    parity: date.Parity);
                LessonBase mergedBase = new(mergedData);
                lessons[item.Id.Id] = new(mergedBase, mergedDate);
            }

            void Diagnose(string reason)
            {
                diagnostics.Add(new(
                    key.Course,
                    key.Group,
                    key.Specialization,
                    key.Alternative,
                    reason));
            }
        }
        var weeklyLessons = lessons.ToImmutable();
        var automaticMerges = merges.ToImmutable();
        var mergeDiagnostics = diagnostics.ToImmutable();
        var suppressedLessons = suppressed.ToImmutable();
        return new(
            weeklyLessons: weeklyLessons,
            oneTimeLessons: source.OneTimeLessons,
            groups: source.Groups,
            teachers: source.Teachers,
            courses: source.Courses,
            periods: source.Periods,
            automaticLabSubGroupMerges: automaticMerges,
            labSubGroupMergeDiagnostics: mergeDiagnostics,
            suppressedWeeklyLessons: suppressedLessons);
    }

    private static bool IsNumeric(SubGroup subgroup) =>
        subgroup.Value is { } value && NumberHelper.FromRoman(value) is not null;
}

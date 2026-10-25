using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib;

public readonly record struct DetectedLabSubGroupMerge(
    CourseId Course, GroupId Group, Specialization Specialization, Alternative Alternative,
    SubGroup SurvivingSubGroup, PeriodId EvidencePeriod);

public readonly record struct LabSubGroupMergeDiagnostic(
    CourseId Course, GroupId Group, Specialization Specialization, Alternative Alternative, string Reason);

/// <summary>
/// Derives a semester-wide whole-group audience from a newer timetable replacing
/// numeric lab subgroups. Raw source lessons and their lookup IDs stay intact.
/// </summary>
public static class LabSubGroupMerges
{
    private readonly record struct Key(CourseId Course, GroupId Group,
        Specialization Specialization, Alternative Alternative);

    public static Schedule Project(Schedule source, bool enabled)
    {
        bool conditionResult;
        if (!enabled)
        {
            conditionResult = true;
        }
        else
        {
            conditionResult = source.AutomaticLabSubGroupMerges.IsEmpty;
            conditionResult = !(conditionResult);
        }

        if (conditionResult)
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
        var candidates = labs.Where(x => { if (!(x.Lesson.Groups.IsSingleGroup)) { return false; } if (!(!x.Lesson.Groups.IsEmpty)) { return false; } return true; })
            .GroupBy(x => new Key(x.Lesson.Course, x.Lesson.Group, x.Lesson.Specialization, x.Lesson.Alternative));

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
            if (candidate.Any(x => { bool CalculateConditionResult() { if (x.Date.Period.IsUnspecified) { if (IsNumeric(x.Lesson.SubGroup)) { return true; } else { if (x.Lesson.SubGroup == SubGroup.All) { return true; } return false; } } else { return false; } } bool conditionResult = CalculateConditionResult(); if (conditionResult) { return true; } return false; })
                || applicable.Zip(applicable.Skip(1))
                    .Any(pair =>
                    source.Get(pair.First).End != source.Get(pair.Second).Start
                    || source.Get(pair.First).Start >= source.Get(pair.Second).Start))
            {
                Diagnose("Period evidence is undated, overlapping, or incomplete.");
                continue;
            }
            if (labs.Any(x =>
            {
                bool CheckCondition()
                {
                    if (!(!x.Lesson.Groups.IsSingleGroup))
                    {
                        return false;
                    }

                    if (!(x.Lesson.Groups.Contains(key.Group)))
                    {
                        return false;
                    }

                    if (!(x.Lesson.Course == key.Course))
                    {
                        return false;
                    }

                    if (!(x.Lesson.Specialization == key.Specialization))
                    {
                        return false;
                    }

                    if (!(x.Lesson.Alternative == key.Alternative))
                    {
                        return false;
                    }

                    return true;
                }

                return CheckCondition();
            }))
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
            bool CalculateConditionResult2()
            {
                if (labels.Length == 0)
                {
                    if (whole.Length == 0)
                    {
                        return true;
                    }

                    return false;
                }
                else
                {
                    return false;
                }
            }

            bool conditionResult2 = CalculateConditionResult2();


            if (conditionResult2)
            {
                Diagnose("The latest group period has no replacement labs for this course partition.");
                continue;
            }
            bool CalculateConditionResult3()
            {
                if (labels.Length == 1)
                {
                    if (whole.Length != 0)
                    {
                        return true;
                    }

                    return false;
                }
                else
                {
                    return false;
                }
            }

            bool conditionResult3 = CalculateConditionResult3();


            if (conditionResult3)
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
                var previousLabs = candidate.Where(x => { bool CalculateConditionResult() { if (x.Date.Period == previous) { if (IsNumeric(x.Lesson.SubGroup)) { return true; } else { if (x.Lesson.SubGroup == SubGroup.All) { return true; } return false; } } else { return false; } } bool conditionResult = CalculateConditionResult(); if (conditionResult) { return true; } return false; })
                    .ToArray();
                var previousLabels = previousLabs.Where(x => IsNumeric(x.Lesson.SubGroup))
                    .Select(x => x.Lesson.SubGroup)
                    .Distinct()
                    .ToArray();
                if (previousLabels.Length >= 2)
                { before = previousLabs; break; }
                bool CalculateConditionResult5()
                {
                    if (previousLabs.Length == 0)
                    {
                        return true;
                    }
                    else
                    {
                        if (labels.Length == 1)
                        {
                            if (previousLabels.Length == 1)
                            {
                                if (previousLabels[0] != labels[0])
                                {
                                    return true;
                                }

                                return false;
                            }
                            else
                            {
                                return false;
                            }
                        }
                        else
                        {
                            return false;
                        }
                    }
                }

                bool conditionResult5 = CalculateConditionResult5();


                if (conditionResult5)
                {
                    valid = false;
                    break;
                }
            }
            bool CalculateConditionResult4()
            {
                if (!valid)
                {
                    return true;
                }
                else
                {
                    if (before.Length == 0)
                    {
                        return true;
                    }

                    return false;
                }
            }

            bool conditionResult4 = CalculateConditionResult4();


            if (conditionResult4)
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
            global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.DetectedLabSubGroupMerge>.Builder builder = merges;
            global::ScheduleLib.DetectedLabSubGroupMerge item2 = new(key.Course, key.Group, key.Specialization, key.Alternative, survivor, latest);
            builder.Add(item2);
            foreach (var old in candidate.Where(x => IsNumeric(x.Lesson.SubGroup) || x.Lesson.SubGroup == SubGroup.All))
            {
                suppressed.Add(old.Id);
            }

            foreach (var item in retained)
            {
                suppressed.Remove(item.Id);
                var lesson = lessons[item.Id.Id];
                lesson.Base = lesson.Base with
                {
                    Data = lesson.Base.Data with
                    {
                        SubGroup = SubGroup.All,
                    },
                };
                lesson.Date = lesson.Date with
                {
                    Period = PeriodId.Unspecified,
                };
                lessons[item.Id.Id] = lesson;
            }

            void Diagnose(string reason)
            {
                global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.LabSubGroupMergeDiagnostic>.Builder builder = diagnostics;
                global::ScheduleLib.LabSubGroupMergeDiagnostic item = new(
                    key.Course, key.Group, key.Specialization, key.Alternative, reason);
                builder.Add(item);
            }
        }
        return new()
        {
            WeeklyLessons = lessons.ToImmutable(),
            OneTimeLessons = source.OneTimeLessons,
            Groups = source.Groups,
            Teachers = source.Teachers,
            Courses = source.Courses,
            Periods = source.Periods,
            AutomaticLabSubGroupMerges = merges.ToImmutable(),
            LabSubGroupMergeDiagnostics = diagnostics.ToImmutable(),
            SuppressedWeeklyLessons = suppressed.ToImmutable(),
        };
    }

    private static bool IsNumeric(SubGroup subgroup) =>
        subgroup.Value is { } value && NumberHelper.FromRoman(value) is not null;
}

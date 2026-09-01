using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib;

/// <summary>
/// One generated student selection: at most one value per active partition of a group.
/// A null partition is inactive for the group and is omitted from names.
/// </summary>
public readonly record struct GroupCombination(
    Specialization? Specialization,
    SubGroup? Proficiency,
    SubGroup? Language,
    SubGroup? Numeric)
{
    /// <summary>
    /// The subgroup values the combination selects. A lesson's subgroup must be
    /// <see cref="SubGroup.All"/> or equal one of these to be included.
    /// </summary>
    public IEnumerable<SubGroup> SelectedSubGroups()
    {
        if (Proficiency is { } p)
        {
            yield return p;
        }
        if (Language is { } l)
        {
            yield return l;
        }
        if (Numeric is { } n)
        {
            yield return n;
        }
    }

    /// <summary>
    /// Appends the selected values in the canonical name order:
    /// specialization-proficiency-language-numeric.
    /// </summary>
    public void AppendFileNamePart(ListStringBuilder lb)
    {
        if (Specialization is { } s)
        {
            lb.Append(s.Value!);
        }
        if (Proficiency is { } p)
        {
            lb.Append(p.Value!);
        }
        if (Language is { } l)
        {
            lb.Append(l.Value!);
        }
        if (Numeric is { } n)
        {
            lb.Append(n.Value!);
        }
    }
}

/// <summary>
/// The split state of one group: the observed specializations and every generated
/// student combination. Matching uses this context, because the same stored lesson
/// may behave differently per group (a singleton specialization behaves as shared).
/// </summary>
public sealed class GroupSplitInfo
{
    // Two or more observed specializations activate the specialization partition.
    public required ImmutableArray<Specialization> ObservedSpecializations { get; init; }
    public bool SpecializationActive => ObservedSpecializations.Length >= 2;
    public required ImmutableArray<GroupCombination> Combinations { get; init; }

    public bool IncludesLesson(in GroupCombination combination, in LessonData lesson)
    {
        if (!IncludesSpecialization(in combination, in lesson))
        {
            return false;
        }
        return IncludesSubGroup(in combination, in lesson);
    }

    private bool IncludesSpecialization(in GroupCombination combination, in LessonData lesson)
    {
        if (lesson.Specialization == Specialization.All)
        {
            return true;
        }
        if (!SpecializationActive)
        {
            // Inactive: the annotation behaves as shared for this group.
            return true;
        }
        return combination.Specialization is { } selected
            && selected == lesson.Specialization;
    }

    private static bool IncludesSubGroup(in GroupCombination combination, in LessonData lesson)
    {
        if (lesson.SubGroup == SubGroup.All)
        {
            return true;
        }
        foreach (var selected in combination.SelectedSubGroups())
        {
            if (selected == lesson.SubGroup)
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class GroupSplitInfoByGroup : Dictionary<GroupId, GroupSplitInfo>
{
    private GroupSplitInfoByGroup()
    {
    }

    public static GroupSplitInfoByGroup Build(
        Schedule schedule,
        SpecializationRegistry? registry)
    {        var ret = new GroupSplitInfoByGroup();

        var numeric = new Dictionary<GroupId, SortedSet<int>>();
        var languages = new Dictionary<GroupId, SortedSet<SubGroup>>();
        var hasBeginners = new HashSet<GroupId>();
        var specializations = new Dictionary<GroupId, HashSet<Specialization>>();
        var groupModels = new Dictionary<GroupId, Group>();

        foreach (var g in schedule.EnumerateGroups())
        {
            groupModels[g.Id] = g.Item;
            numeric[g.Id] = [];
            languages[g.Id] = [];
            specializations[g.Id] = [];
        }

        foreach (var l in schedule.EnumerateAllLessons())
        {
            ref readonly var lesson = ref l.Lesson;
            var splitKey = lesson.GroupSplitKey;
            foreach (var groupId in lesson.Groups)
            {
                if (NumberHelper.FromRoman(splitKey.SubGroup.Value) is { } number)
                {
                    numeric[groupId].Add(number);
                }
                else if (splitKey.SubGroup == SpecialSubGroups.Ro
                    || splitKey.SubGroup == SpecialSubGroups.Ru
                    || splitKey.SubGroup == SpecialSubGroups.Eng)
                {
                    languages[groupId].Add(splitKey.SubGroup);
                }
                else if (splitKey.SubGroup == SpecialSubGroups.Beginners)
                {
                    hasBeginners.Add(groupId);
                }

                if (splitKey.Specialization != Specialization.All)
                {
                    specializations[groupId].Add(splitKey.Specialization);
                }
            }
        }

        foreach (var (groupId, observedSpecs) in specializations)
        {
            var group = groupModels[groupId];

            ImmutableArray<Specialization> permitted = [.. observedSpecs];
            if (registry is { } r)
            {
                var allowed = r.PermittedFor(in group);
                permitted = [.. observedSpecs.Where(allowed.Contains)];
            }
            var specValues = permitted
                .OrderBy(x => x.Value, StringComparer.Ordinal)
                .ToArray();

            var langs = languages[groupId]
                .OrderBy(x => x.Value, StringComparer.Ordinal)
                .ToArray();
            var nums = numeric[groupId]
                .OrderBy(x => x)
                .Select(SubGroup.CreateNumeric)
                .ToArray();
            SubGroup[] profValues = hasBeginners.Contains(groupId)
                ? [SpecialSubGroups.Beginners, SpecialSubGroups.NonBeginners]
                : [];

            var combinations = ImmutableArray.CreateBuilder<GroupCombination>();
            foreach (var spec in OneOrNone(specValues))
            {
                foreach (var proficiency in OneOrNone(profValues))
                {
                    foreach (var language in OneOrNone(langs))
                    {
                        foreach (var num in OneOrNone(nums))
                        {
                            combinations.Add(new(spec, proficiency, language, num));
                        }
                    }
                }
            }

            // A group with no active split has no suffixed combinations at all;
            // it only gets its whole-group schedule.
            if (combinations.Count == 1 && combinations[0] == default)
            {
                combinations.Clear();
            }

            ret[groupId] = new()
            {
                ObservedSpecializations = [.. observedSpecs],
                Combinations = combinations.MoveToImmutable(),
            };
        }

        return ret;

        static IEnumerable<T?> OneOrNone<T>(T[] values)
            where T : struct
        {
            if (values.Length == 0)
            {
                yield return null;
                yield break;
            }
            foreach (var v in values)
            {
                yield return v;
            }
        }
    }
}

public static class GroupSplitInfoHelper
{
    /// <summary>
    /// Discovers the active partitions and generated combinations of every group.
    /// A null registry permits every observed specialization value.
    /// </summary>
    public static GroupSplitInfoByGroup GetGroupSplitInfo(
        this Schedule schedule,
        SpecializationRegistry? registry = null)
    {
        return GroupSplitInfoByGroup.Build(schedule, registry);
    }
}

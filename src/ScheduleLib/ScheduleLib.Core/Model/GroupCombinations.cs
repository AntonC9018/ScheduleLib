using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib;

/// <summary>
/// One generated student selection: at most one value per active partition of a group.
/// A null partition is inactive for the group and is omitted from names.
/// </summary>
/// <remarks>
/// The proficiency, language and numeric selections stay <see cref="SubGroup"/> (rather
/// than a dedicated language/number type) because lessons store the whole subgroup
/// partition as one <see cref="SubGroup"/> value (<see cref="LessonData.SubGroup"/>),
/// and <see cref="GroupFilter.SubGroups"/> matches on that same type. The
/// <see cref="Language"/> enum is unrelated: it is the group's teaching language,
/// not a subgroup partition value. There is no number value type; numerics are
/// <see cref="SubGroup"/> values built with <see cref="SubGroup.CreateNumeric(int)"/>.
/// </remarks>
public readonly record struct GroupCombination(
    Specialization? Specialization = null,
    SubGroup? Proficiency = null,
    SubGroup? Language = null,
    SubGroup? Numeric = null,
    Alternative? Alternative = null)
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
    /// Builds the <see cref="GroupFilter"/> selecting exactly this combination's
    /// lessons. A null selection means the partition is inactive for the
    /// group, so no restriction is applied: this keeps the filter consistent
    /// with <see cref="GroupPartitionInfo.IncludesLesson"/>, where an inactive
    /// partition (including a registry-deactivated one) behaves as shared.
    /// Passing an empty array instead would wrongly drop those lessons,
    /// because <c>FilteredSchedule</c> counts raw observed values.
    /// </summary>
    public GroupFilter ToGroupFilter(GroupId groupId)
    {
        var selectedSubGroups = new List<SubGroup>
        {
            SubGroup.All,
        };
        selectedSubGroups.AddRange(SelectedSubGroups());
        return new GroupFilter
        {
            OneOfGroupIds = [groupId],
            SubGroups = [.. selectedSubGroups],
            Specializations = Specialization is { } spec
                ? [spec]
                : null,
            Alternatives = Alternative is { } alternative
                ? [alternative]
                : null,
        };
    }

    /// <summary>
    /// Appends the selected values in the canonical name order:
    /// alternative-specialization-proficiency-language-numeric.
    /// </summary>
    public void AppendFileNamePart(ListStringBuilder lb)
    {
        if (Alternative is { } a)
        {
            lb.Append(a.Value!);
        }
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
/// The partition state of one group: the observed specializations and every generated
/// student combination. Matching uses this context, because the same stored lesson
/// may behave differently per group (a singleton specialization behaves as shared).
/// </summary>
public sealed class GroupPartitionInfo
{
    // Two or more registry-permitted observed specializations activate the
    // specialization partition. A null registry permits every observed value;
    // otherwise filtered-out values behave as shared for the group.
    // This stores the permitted subset only, not the raw observed set, so it
    // diverges from FilteredSchedule raw observed counts by design.
    public required ImmutableArray<Specialization> PermittedSpecializations { get; init; }
    public bool SpecializationActive => PermittedSpecializations.Length >= 2;
    // Same activation rule for the alternative partition.
    public required ImmutableArray<Alternative> ObservedAlternatives { get; init; }
    public bool AlternativeActive => ObservedAlternatives.Length >= 2;
    public required ImmutableArray<GroupCombination> Combinations { get; init; }

    public bool IncludesLesson(in GroupCombination combination, in LessonData lesson)
    {
        if (!IncludesAlternative(in combination, in lesson))
        {
            return false;
        }
        if (!IncludesSpecialization(in combination, in lesson))
        {
            return false;
        }
        return IncludesSubGroup(in combination, in lesson);
    }

    private bool IncludesAlternative(in GroupCombination combination, in LessonData lesson)
    {
        if (lesson.Alternative == Alternative.All)
        {
            return true;
        }
        if (!AlternativeActive)
        {
            // Inactive: the annotation behaves as shared for this group.
            return true;
        }
        return combination.Alternative is { } selected
            && selected == lesson.Alternative;
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

public sealed class GroupPartitionInfoByGroup : Dictionary<GroupId, GroupPartitionInfo>
{
    private GroupPartitionInfoByGroup()
    {
    }

    public static GroupPartitionInfoByGroup Build(
        Schedule schedule,
        SpecializationRegistry? registry)
    {
        var ret = new GroupPartitionInfoByGroup();

        var numeric = new Dictionary<GroupId, SortedSet<int>>();
        var languages = new Dictionary<GroupId, HashSet<SubGroup>>();
        var hasBeginners = new HashSet<GroupId>();
        var specializations = new Dictionary<GroupId, HashSet<Specialization>>();
        var alternatives = new Dictionary<GroupId, HashSet<Alternative>>();
        var groupModels = new Dictionary<GroupId, Group>();

        foreach (var g in schedule.EnumerateGroups())
        {
            groupModels[g.Id] = g.Item;
            numeric[g.Id] = [];
            languages[g.Id] = [];
            specializations[g.Id] = [];
            alternatives[g.Id] = [];
        }

        foreach (var l in schedule.EnumerateAllLessons())
        {
            ref readonly var lesson = ref l.Lesson;
            var partitionKey = lesson.GroupPartitionKey;
            foreach (var groupId in lesson.Groups)
            {
                if (NumberHelper.FromRoman(partitionKey.SubGroup.Value) is { } number)
                {
                    numeric[groupId].Add(number);
                }
                else if (SpecialSubGroups.IsLanguageSubGroup(partitionKey.SubGroup))
                {
                    languages[groupId].Add(partitionKey.SubGroup);
                }
                else if (partitionKey.SubGroup == SpecialSubGroups.Beginners)
                {
                    hasBeginners.Add(groupId);
                }

                if (partitionKey.Specialization != Specialization.All)
                {
                    specializations[groupId].Add(partitionKey.Specialization);
                }
                if (partitionKey.Alternative != Alternative.All)
                {
                    alternatives[groupId].Add(partitionKey.Alternative);
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
            // A singleton specialization is stored on the lesson but is shared for
            // this group. It must not create a filename dimension or a student
            // combination. Activation follows the registry-permitted subset: if
            // filtering leaves fewer than two permitted values, the partition is
            // inactive and the filtered-out values behave as shared instead of
            // vanishing from every combination.
            var specValues = permitted.Length < 2
                ? []
                : permitted
                    .OrderBy(x => x.Value, StringComparer.Ordinal)
                    .ToArray();

            var observedAlts = alternatives[groupId];
            // The alternative partition follows the same activation rule: a single
            // observed alternative behaves as shared and creates no combinations.
            var altValues = observedAlts.Count < 2
                ? []
                : observedAlts
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
            foreach (var alt in OneOrNone(altValues))
            {
                foreach (var spec in OneOrNone(specValues))
                {
                    foreach (var num in OneOrNone(nums))
                    {
                        foreach (var proficiency in OneOrNone(profValues))
                        {
                            foreach (var language in OneOrNone(langs))
                            {
                                combinations.Add(new(spec, proficiency, language, num, alt));
                            }
                        }
                    }
                }
            }

            // A group with no active partition has no suffixed combinations at all;
            // it only gets its whole-group schedule.
            if (combinations.Count == 1 && combinations[0] == default)
            {
                combinations.Clear();
            }

            ret[groupId] = new()
            {
                PermittedSpecializations = [.. permitted.OrderBy(x => x.Value, StringComparer.Ordinal)],
                ObservedAlternatives = [.. observedAlts.OrderBy(x => x.Value, StringComparer.Ordinal)],
                Combinations = combinations.ToImmutable(),
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

public static class GroupPartitionInfoHelper
{
    /// <summary>
    /// Discovers the active partitions and generated combinations of every group.
    /// A null registry permits every observed specialization value.
    /// </summary>
    public static GroupPartitionInfoByGroup GetGroupPartitionInfo(
        this Schedule schedule,
        SpecializationRegistry? registry = null)
    {
        return GroupPartitionInfoByGroup.Build(schedule, registry);
    }
}

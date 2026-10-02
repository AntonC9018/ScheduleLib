using System.Collections.Immutable;
using ScheduleLib.Helper;

namespace ScheduleLib;

/// <summary>
/// Injectable prefix matcher over subgroup/specialization labels.
/// Follows the <see cref="Parsing.Lesson.RoomParser"/> precedent: a concrete
/// sealed class with a <see cref="Default"/> instance, taken via ctor/param
/// (<see cref="Parsing.WordDoc.DocParseContext"/>). The parser uses the
/// injected instance, so it no longer depends on the exact
/// <see cref="Specializations.AllKnown"/> list; callers with their own labels
/// supply their own candidate set.
/// </summary>
public sealed class SubGroupPrefixMatcher
{
    public static readonly SubGroupPrefixMatcher Default = new(CreateDefaultCandidates());

    private readonly ImmutableArray<SubGroup> _prefixCandidates;

    public SubGroupPrefixMatcher(ImmutableArray<SubGroup> prefixCandidates)
    {
        _prefixCandidates = prefixCandidates;
    }

    // Group headers may name any known label. Specialization values are transported as subgroups
    // here and get classified into Specialization during builder processing.
    // Specializations must come before the legacy values, so that prefixes like "ui" keep matching
    // the specialization instead of "UI-1".
    private static ImmutableArray<SubGroup> CreateDefaultCandidates()
    {
        // Explicit composition, not reflection: the canonical values live in
        // ScheduleDefaults (config layer), which Core cannot reference without
        // inverting the layering (and OnlineRegistry cannot reference
        // ScheduleDefaults back). Reflecting over that assembly by name from
        // Core would be fragile; the injected seam above is what decouples the
        // parser from the exact list.
        var builder = ImmutableArray.CreateBuilder<SubGroup>();
        builder.Add(SpecialSubGroups.Optional);
        builder.Add(SpecialSubGroups.Beginners);
        builder.Add(SpecialSubGroups.NonBeginners);
        builder.Add(SpecialSubGroups.Ru);
        builder.Add(SpecialSubGroups.Ro);
        builder.Add(SpecialSubGroups.Eng);
        foreach (var specialization in Specializations.AllKnown)
        {
            builder.Add(new(specialization.Value!));
        }
        builder.AddRange(SpecialSubGroups.Legacy);
        // MoveToImmutable would only work while the item count happens to equal the
        // builder capacity, so copy instead.
        return builder.ToImmutable();
    }

    public bool TryFromNamePrefix(ReadOnlySpan<char> value, out SubGroup subGroup)
    {
        value = value.Trim();
        if (value.EndsWith('.'))
        {
            value = value[..^1];
        }

        const int minimumPrefixLength = 2;
        if (value.Length < minimumPrefixLength)
        {
            subGroup = default;
            return false;
        }

        foreach (var candidate in _prefixCandidates)
        {
            if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(candidate.Value!, value))
            {
                subGroup = candidate;
                return true;
            }
        }

        foreach (var candidate in _prefixCandidates)
        {
            if (IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(candidate.Value!, value))
            {
                subGroup = candidate;
                return true;
            }
        }

        subGroup = default;
        return false;
    }
}

public static class SpecialSubGroups
{
    // The meaning of these historical labels is unresolved.
    // They stay ordinary subgroup values: don't split their digits and don't treat them as specializations.
    public static readonly ImmutableArray<SubGroup> Legacy = [
        new("S1"),
        new("S11"),
        new("S12"),
        new("S21"),
        new("S22"),
        new("S23"),
        new("GA"),
        new("GA1"),
        new("GA2"),
        new("WR"),
        new("WR1"),
        new("WR2"),
        new("SF"),
        new("UI-1"),
        new("UI-2"),
    ];

    public static readonly ImmutableArray<SubGroup> AllSpecial = [
        Optional,
        Beginners,
        NonBeginners,
        Ru,
        Ro,
        Eng,
        ..Legacy,
    ];
    // Legacy marker for an unspecified specialization subgroup. New schedules should name the specialization.
    public static SubGroup Optional => new("opțional");
    public static SubGroup Beginners => new("începători");
    public static SubGroup NonBeginners => new("nuîncepători");
    public static SubGroup Ru => new("ru");
    public static SubGroup Ro => new("ro");
    public static SubGroup Eng => new("eng");

    /// <summary>
    /// Back-compat forward to <see cref="SubGroupPrefixMatcher.Default"/>.
    /// Parser paths take an injected <see cref="SubGroupPrefixMatcher"/> instead.
    /// </summary>
    public static bool TryFromNamePrefix(ReadOnlySpan<char> value, out SubGroup subGroup)
    {
        return SubGroupPrefixMatcher.Default.TryFromNamePrefix(value, out subGroup);
    }

    public static SubGroup FromLanguage(Language lang)
    {
        return lang switch
        {
            Language.Ro => Ro,
            Language.Ru => Ru,
            Language.En => Eng,
            _ => throw new ArgumentOutOfRangeException(nameof(lang)),
        };
    }

    public static bool IsLanguageSubGroup(this SubGroup subGroup)
    {
        return subGroup == Ro || subGroup == Ru || subGroup == Eng;
    }
}

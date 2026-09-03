using System.Collections.Immutable;

namespace ScheduleLib;

/// <summary>
/// The group category fields a registry selector may constrain.
/// A field left null matches every value in that category.
/// </summary>
public sealed class GroupSelectorBuilder
{
    public Grade? Grade { get; set; }
    public Faculty? Faculty { get; set; }
    public AttendanceMode? AttendanceMode { get; set; }
    public QualificationType? Qualification { get; set; }
}

public readonly record struct GroupCategorySelector(
    Grade? Grade,
    Faculty? Faculty,
    AttendanceMode? AttendanceMode,
    QualificationType? Qualification)
{
    public bool Matches(in Group group)
    {
        if (Grade is { } grade
            && group.Grade != grade)
        {
            return false;
        }
        if (Faculty is { } faculty
            && group.Faculty != faculty)
        {
            return false;
        }
        if (AttendanceMode is { } attendance
            && group.AttendanceMode != attendance)
        {
            return false;
        }
        if (Qualification is { } qualification
            && group.QualificationType != qualification)
        {
            return false;
        }
        return true;
    }
}

/// <summary>
/// Allowlist of the specialization values permitted for categories of groups.
/// Overlapping selectors union their sets. Registry membership permits a value;
/// it does not activate the specialization for a group.
/// </summary>
public sealed class SpecializationRegistry
{
    private readonly ImmutableArray<(GroupCategorySelector Selector, ImmutableArray<Specialization> Values)> _entries;

    internal SpecializationRegistry(
        ImmutableArray<(GroupCategorySelector Selector, ImmutableArray<Specialization> Values)> entries)
    {
        _entries = entries;
    }

    public static SpecializationRegistry Empty { get; } = new([]);

    /// <summary>
    /// The union of the sets of every selector matching the group.
    /// No matching entry means no specialization values are allowed.
    /// </summary>
    public ImmutableArray<Specialization> PermittedFor(in Group group)
    {
        var builder = ImmutableArray.CreateBuilder<Specialization>();
        var seen = new HashSet<Specialization>();
        foreach (var (selector, values) in _entries)
        {
            if (!selector.Matches(in group))
            {
                continue;
            }
            foreach (var value in values)
            {
                if (seen.Add(value))
                {
                    builder.Add(value);
                }
            }
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Finds a value registered by any selector. Parsing uses this to distinguish
    /// configured future specializations from ordinary subgroup labels; per-group
    /// allowlisting still happens during combination generation.
    /// </summary>
    public bool TryFromValue(string? value, out Specialization specialization)
    {
        if (value is not null)
        {
            foreach (var (_, values) in _entries)
            {
                foreach (var candidate in values)
                {
                    if (string.Equals(candidate.Value, value, StringComparison.Ordinal))
                    {
                        specialization = candidate;
                        return true;
                    }
                }
            }
        }

        specialization = default;
        return false;
    }

    /// <summary>
    /// Describes the selectors that apply to a group. This is intended for source
    /// validation errors, where seeing the exact registry context is more useful
    /// than a bare "unknown subgroup" message.
    /// </summary>
    public string DescribeMatches(in Group group)
    {
        var matches = new List<string>();
        foreach (var (selector, values) in _entries)
        {
            if (!selector.Matches(in group))
            {
                continue;
            }

            var constraints = new List<string>(4);
            if (selector.Grade is { } grade)
            {
                constraints.Add($"grade {grade.Value}");
            }
            if (selector.Faculty is { } faculty)
            {
                constraints.Add($"faculty {faculty.Name}");
            }
            if (selector.AttendanceMode is { } attendance)
            {
                constraints.Add($"attendance {attendance}");
            }
            if (selector.Qualification is { } qualification)
            {
                constraints.Add($"qualification {qualification}");
            }

            var selectorText = constraints.Count == 0
                ? "any group"
                : string.Join(", ", constraints);
            matches.Add($"({selectorText}) => [{string.Join(", ", values.Select(x => x.Value))}]");
        }

        return matches.Count == 0
            ? "no matching selector"
            : string.Join("; ", matches);
    }
}

public sealed class SpecializationRegistryBuilder()
{
    // Set reuse: identical value lists share one array instance.
    private readonly Dictionary<string, ImmutableArray<Specialization>> _sets = new(StringComparer.Ordinal);
    private readonly List<(GroupCategorySelector Selector, ImmutableArray<Specialization> Values)> _entries = new();

    public SpecializationSetBuilder Set(params ReadOnlySpan<Specialization> values)
    {
        var array = Intern(values);
        return new(this, array);
    }

    public SpecializationRegistry Build()
    {
        return new([.. _entries]);
    }

    internal void Add(GroupCategorySelector selector, ImmutableArray<Specialization> values)
    {
        _entries.Add((selector, values));
    }

    private ImmutableArray<Specialization> Intern(ReadOnlySpan<Specialization> values)
    {
        var key = string.Join("|", values.ToArray().Select(x => x.Value));
        if (_sets.TryGetValue(key, out var set))
        {
            return set;
        }
        set = [.. values];
        _sets[key] = set;
        return set;
    }
}

public sealed class SpecializationSetBuilder
{
    private readonly SpecializationRegistryBuilder _owner;
    private readonly ImmutableArray<Specialization> _values;

    internal SpecializationSetBuilder(
        SpecializationRegistryBuilder owner,
        ImmutableArray<Specialization> values)
    {
        _owner = owner;
        _values = values;
    }

    public void ApplyTo(Action<GroupSelectorBuilder> configure)
    {
        var b = new GroupSelectorBuilder();
        configure(b);
        _owner.Add(new(
            Grade: b.Grade,
            Faculty: b.Faculty,
            AttendanceMode: b.AttendanceMode,
            Qualification: b.Qualification), _values);
    }
}

public static partial class SpecializationRegistryHelper
{
    /// <summary>
    /// The initial registry. See docs/domain-model.md for the permitted sets.
    /// </summary>
    public static SpecializationRegistry CreateDefault()
    {
        var b = new SpecializationRegistryBuilder();
        b.Set([Specializations.AlgoritmicaGrafurilor, Specializations.Logica])
            .ApplyTo(x =>
            {
                x.Grade = new(1);
                x.Faculty = new("I");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specializations.AlgoritmicaGrafurilor, Specializations.Logica])
            .ApplyTo(x =>
            {
                x.Grade = new(1);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specializations.AlgoritmicaGrafurilor, Specializations.Logica])
            .ApplyTo(x =>
            {
                x.Grade = new(1);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Dual;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specializations.Spring])
            .ApplyTo(x =>
            {
                x.Grade = new(2);
                x.Faculty = new("I");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([
                Specializations.CV,
                Specializations.DJ,
                Specializations.GA2D,
                Specializations.GA3D,
                Specializations.React,
                Specializations.Spring,
                Specializations.SSI,
                Specializations.UI,
            ])
            .ApplyTo(x =>
            {
                x.Grade = new(2);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        b.Set([Specializations.DJ, Specializations.DezvoltareaAplicatiilor])
            .ApplyTo(x =>
            {
                x.Grade = new(3);
                x.Faculty = new("IA");
                x.AttendanceMode = AttendanceMode.Zi;
                x.Qualification = QualificationType.Licenta;
            });
        return b.Build();
    }
}

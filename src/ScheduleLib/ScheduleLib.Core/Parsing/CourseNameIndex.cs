using System.Collections.Frozen;
using System.Collections.Immutable;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Parsing.CourseName;

/// <summary>
/// Finds courses of a built schedule by name: first among their names and aliases, then by the
/// unified form of the name. Lookups never add aliases.
/// </summary>
/// <remarks>
/// Every name of a course is unified, not only the first: unified names compare by prefix,
/// which is not transitive, so a name matching one spelling of a course may miss another.
/// When unified names of several courses match, the course with the lowest id wins,
/// the one imported first.
/// </remarks>
public sealed class CourseNameIndex
{
    private readonly CourseNameUnifierConfig _config;
    private readonly ImmutableArray<SlowCourse> _unifiedNames;

    private CourseNameIndex(
        CourseNameUnifierConfig config,
        FrozenDictionary<string, CourseId> names,
        ImmutableArray<SlowCourse> unifiedNames)
    {
        _config = config;
        Names = names;
        _unifiedNames = unifiedNames;
    }

    /// <summary>Every name and alias of every course.</summary>
    public FrozenDictionary<string, CourseId> Names { get; }

    public static CourseNameIndex Create(ImmutableArray<Course> courses, CourseNameUnifierConfig config)
    {
        var names = new Dictionary<string, CourseId>(StringComparer.CurrentCultureIgnoreCase);
        // Ordered by course id, so the first match is the earliest course.
        var unifiedNames = ImmutableArray.CreateBuilder<SlowCourse>();
        for (int i = 0; i < courses.Length; i++)
        {
            var id = new CourseId(i);
            var course = courses[i];
            foreach (var name in course.Names)
            {
                names.Add(name, id);
                unifiedNames.Add(config.UnifiedCourse(name, id));
            }
        }
        var frozenNames = names.ToFrozenDictionary(StringComparer.CurrentCultureIgnoreCase);
        return new(config, frozenNames, unifiedNames.ToImmutable());
    }

    public CourseId? Find(ReadOnlyMemory<char> courseName, CourseNameParseOptions parseOptions)
    {
        var exactNames = Names.GetAlternateLookup<ReadOnlySpan<char>>();
        if (exactNames.TryGetValue(courseName.Span, out var id))
        {
            return id;
        }

        var unifiedName = _config.ParseUnified(courseName, parseOptions);
        if (CourseNameUnifierConfig.FindUnified(_unifiedNames.AsSpan(), unifiedName) is { } unifiedId)
        {
            return unifiedId;
        }
        return null;
    }
}

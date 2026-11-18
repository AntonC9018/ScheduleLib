using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Parsing.CourseName;

public readonly record struct FullyRenamedCourse(ParsedCourseName From, ParsedCourseName To);

public sealed class CourseNameUnifierConfig
{
    public required CourseNameParserConfig ParserConfig { get; init; }
    private ImmutableArray<FullyRenamedCourse> FullyRemappedNames { get; init; } = [];

    private CourseNameUnifierConfig()
    {
    }

    /// <summary>Parses a course name and applies the full renames: the form names are compared in.</summary>
    public ParsedCourseName ParseUnified(ReadOnlyMemory<char> courseName, CourseNameParseOptions options)
    {
        var parsed = ParserConfig.Parse(courseName, options);
        return TryRemap(parsed);
    }

    /// <summary>The unified form of a course's own name, which may contain punctuation.</summary>
    internal SlowCourse UnifiedCourse(string courseName, CourseId id)
    {
        var options = new CourseNameParseOptions
        {
            IgnorePunctuation = true,
        };
        var parsed = ParseUnified(courseName.AsMemory(), options);
        return new(parsed, id);
    }

    internal static CourseId? FindUnified(ReadOnlySpan<SlowCourse> courses, ParsedCourseName unifiedName)
    {
        // TODO: N^2, use some sort of hash to make this faster.
        foreach (var t in courses)
        {
            if (t.Name.IsEqual(unifiedName))
            {
                return t.CourseId;
            }
        }
        return null;
    }

    public ParsedCourseName TryRemap(ParsedCourseName original)
    {
        foreach (var remap in FullyRemappedNames)
        {
            if (original.IsEqual(remap.From))
            {
                return remap.To;
            }
        }
        return original;
    }

    public static CourseNameUnifierConfig Create(
        CourseNameParserConfig config,
        ReadOnlySpan<CourseNameMapping> fullyRenamedNames)
    {
        var remappedNames = ImmutableArray.CreateBuilder<FullyRenamedCourse>(fullyRenamedNames.Length);
        foreach (var (from, to) in fullyRenamedNames)
        {
            var fromParsed = config.Parse(
                from.AsMemory(),
                new()
                {
                    IgnorePunctuation = true,
                });
            var toParsed = config.Parse(
                to.AsMemory(),
                new()
                {
                    IgnorePunctuation = true,
                });
            var builder = remappedNames;
            var item = new FullyRenamedCourse(From: fromParsed, To: toParsed);
            builder.Add(item);
        }
        return new CourseNameUnifierConfig
        {
            ParserConfig = config,
            FullyRemappedNames = remappedNames.MoveToImmutable(),
        };
    }
}

public sealed class CourseNameUnifierModule
{
    internal readonly List<SlowCourse> SlowCourses = new();
    private readonly CourseNameUnifierConfig _config;

    public CourseNameUnifierModule(CourseNameUnifierConfig config)
    {
        _config = config;
    }

    public void Refresh(ScheduleBuilder builder)
    {
        SlowCourses.Clear();
        var courses = builder.Courses;
        if (builder.LookupModule is not { } lookup)
        {
            Debug.Assert(false, "Lookup module must be enabled before refreshing CourseNameUnifierModule.");
            throw new MissingCourseLookupModuleException();
        }

        for (int i = 0; i < courses.Count; i++)
        {
            var courseId = new CourseId(i);
            var fullName = courses.Ref(i).FullName;
            // Only adding a single instance of this because they are all equivalent.
            SlowCourses.Add(_config.UnifiedCourse(fullName, courseId));
            // Also adding lookup for the future.
            lookup.Courses.TryAdd(fullName, courseId);
        }
    }

    public struct FindOrAddParams()
    {
        public required ScheduleBuilder Schedule;
        public required ReadOnlyMemory<char> CourseName;
        public CourseNameParseOptions ParseOptions = new();

        internal readonly CourseNameForParsing CourseNameForParsing
        {
            get
            {
                return new()
                {
                    CourseName = CourseName,
                    ParseOptions = ParseOptions,
                };
            }
        }
    }

    public struct CourseNameForParsing
    {
        public required CourseNameParseOptions ParseOptions;
        public required ReadOnlyMemory<char> CourseName;
    }

    private ParsedCourseName ParseUnified(CourseNameForParsing p)
    {
        return _config.ParseUnified(p.CourseName, p.ParseOptions);
    }

    private CourseId? FindSlow(ParsedCourseName unifiedName)
    {
        return CourseNameUnifierConfig.FindUnified(CollectionsMarshal.AsSpan(SlowCourses), unifiedName);
    }

    public CourseId FindOrAdd(in FindOrAddParams p)
    {
        var alt = p.Schedule.LookupModule!.Courses.GetAlternateLookup<ReadOnlySpan<char>>();
        ref var courseId = ref CollectionsMarshal.GetValueRefOrAddDefault(
            alt,
            p.CourseName.Span,
            out bool exists);

        if (exists)
        {
            return courseId;
        }

        var parsedCourse = ParseUnified(p.CourseNameForParsing);
        if (FindSlow(parsedCourse) is { } slowCourseId)
        {
            courseId = slowCourseId;
            return slowCourseId;
        }

        var result = p.Schedule.Courses.New();
        courseId = new(result.Id);
        ScheduleBuilderHelper.UpdateLookupAfterCourseAdded(p.Schedule);

        SlowCourses.Add(new(parsedCourse, new(result.Id)));

        return new(result.Id);
    }
}

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Parsing.CourseName;

public readonly struct CourseNameUnifierModuleWithDeps
{
    private readonly CourseNameUnifierModule _a;
    private readonly LookupModule _b;

    public CourseNameUnifierModuleWithDeps(
        CourseNameUnifierModule a,
        LookupModule b)
    {
        _a = a;
        _b = b;
    }

    public CourseId? Find(
        string courseName,
        CourseNameParseOptions? parseOptions = null)
    {
        CourseNameUnifierModule.FindParams p = new()
        {
            Lookup = _b,
            CourseName = courseName,
        };
        if (parseOptions is { } x)
        {
            p.ParseOptions = x;
        }
        return _a.Find(p);
    }
}

public readonly record struct FullyRenamedCourse(ParsedCourseName From, ParsedCourseName To);

public sealed class CourseNameUnifierConfig
{
    public required CourseNameParserConfig ParserConfig { get; init; }
    public ImmutableArray<FullyRenamedCourse> FullyRemappedNames { get; init; } = [];

    public static CourseNameUnifierConfig Create(
        CourseNameParserConfig config,
        ReadOnlySpan<(string From, string To)> fullyRenamedNames)
    {
        var remappedNames = ImmutableArray.CreateBuilder<FullyRenamedCourse>(fullyRenamedNames.Length);
        foreach (var (from, to) in fullyRenamedNames)
        {
            var fromParsed = config.Parse(from);
            var toParsed = config.Parse(to);
            remappedNames.Add(new FullyRenamedCourse(fromParsed, toParsed));
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
    private readonly CourseNameParserConfig _parserConfig;
    private readonly ImmutableArray<FullyRenamedCourse> _fullyRemappedNames;

    public CourseNameUnifierModule(CourseNameUnifierConfig config)
    {
        _parserConfig = config.ParserConfig;
        _fullyRemappedNames = config.FullyRemappedNames;
    }

    public void Refresh(ScheduleBuilder builder)
    {
        SlowCourses.Clear();
        var courses = builder.Courses;
        var lookup = builder.LookupModule!;

        for (int i = 0; i < courses.Count; i++)
        {
            var courseId = new CourseId(i);
            var fullName = courses.Ref(i).FullName;
            // Only adding a single instance of this because they are all equivalent.
            AddSlow(fullName, courseId);
            // Also adding lookup for the future.
            lookup.Courses.TryAdd(fullName, courseId);
        }
    }

    private ParsedCourseName TryRemap(ParsedCourseName original)
    {
        foreach (var remap in _fullyRemappedNames)
        {
            if (original.IsEqual(remap.From))
            {
                return remap.To;
            }
        }
        return original;
    }

    public void AddSlow(string courseName, CourseId id)
    {
        var parsedCourse = ParseCourseName(new()
        {
            CourseName = courseName,
            ParseOptions = new()
            {
                IgnorePunctuation = true,
            },
        });
        parsedCourse = TryRemap(parsedCourse);
        SlowCourses.Add(new(parsedCourse, id));
    }

    public ref struct FindParams()
    {
        public required LookupModule Lookup;
        public required string CourseName;
        public CourseNameParseOptions ParseOptions = new();

        internal readonly CourseNameForParsing CourseNameForParsing => new()
        {
            CourseName = CourseName,
            ParseOptions = ParseOptions,
        };
    }

    public CourseId? Find(FindParams p)
    {
        if (p.Lookup.Courses.TryGetValue(p.CourseName, out var courseId))
        {
            return courseId;
        }

        var parsedCourseName = ParseCourseName(p.CourseNameForParsing);
        if (FindSlow(parsedCourseName) is { } slowCourseId)
        {
            p.Lookup.Courses.Add(p.CourseName, slowCourseId);
            return slowCourseId;
        }

        return null;
    }

    public struct FindOrAddParams()
    {
        public required ScheduleBuilder Schedule;
        public required string CourseName;
        public CourseNameParseOptions ParseOptions = new();

        internal readonly CourseNameForParsing CourseNameForParsing => new()
        {
            CourseName = CourseName,
            ParseOptions = ParseOptions,
        };
    }

    public struct CourseNameForParsing
    {
        public required CourseNameParseOptions ParseOptions;
        public required string CourseName;
    }

    private ParsedCourseName ParseCourseName(CourseNameForParsing p)
    {
        var parsedCourse = _parserConfig.Parse(p.CourseName, p.ParseOptions);
        return parsedCourse;
    }

    private CourseId? FindSlow(ParsedCourseName parsedCourseName)
    {
        parsedCourseName = TryRemap(parsedCourseName);

        // TODO: N^2, use some sort of hash to make this faster.
        foreach (var t in SlowCourses)
        {
            if (!t.Name.IsEqual(parsedCourseName))
            {
                continue;
            }
            return t.CourseId;
        }
        return null;
    }

    public CourseId FindOrAdd(in FindOrAddParams p)
    {
        ref var courseId = ref CollectionsMarshal.GetValueRefOrAddDefault(
            p.Schedule.LookupModule!.Courses,
            p.CourseName,
            out bool exists);

        if (exists)
        {
            return courseId;
        }

        var parsedCourse = ParseCourseName(p.CourseNameForParsing);
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

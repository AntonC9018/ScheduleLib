using System.Runtime.InteropServices;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Parsing.CourseName;

public sealed class CourseNameUnifierModule
{
    internal readonly List<SlowCourse> SlowCourses = new();
    private readonly CourseNameParserConfig _parserConfig;

    public CourseNameUnifierModule(CourseNameParserConfig parserConfig)
    {
        _parserConfig = parserConfig;
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
            return new(courseId);
        }

        var parsedCourseName = ParseCourseName(p.CourseNameForParsing);
        if (FindSlow(parsedCourseName) is { } slowCourseId)
        {
            p.Lookup.Courses.Add(p.CourseName, slowCourseId.Id);
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
            return new(courseId);
        }

        var parsedCourse = ParseCourseName(p.CourseNameForParsing);
        if (FindSlow(parsedCourse) is { } slowCourseId)
        {
            courseId = slowCourseId.Id;
            return slowCourseId;
        }

        var result = p.Schedule.Courses.New();
        courseId = result.Id;
        ScheduleBuilderHelper.UpdateLookupAfterCourseAdded(p.Schedule);

        SlowCourses.Add(new(parsedCourse, new(result.Id)));

        return new(result.Id);
    }
}

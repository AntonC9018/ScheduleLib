using System.Diagnostics;
using ScheduleLib.Parsing.Common;
using TruePath;

namespace ScheduleLib.Application.Core;

public static class ScheduleDirectoryDiscovery
{
    public static IEnumerable<ScheduleDirectoryDescriptor> DiscoverDirectories(
        LocalPath path)
    {
        foreach (var dir in Directory.EnumerateDirectories(path.Value))
        {
            var relativePath = dir.AsMemory(path.Value.Length + 1);
            if (ParseFirst(relativePath) is not { } t)
            {
                continue;
            }
            foreach (var subdir in Directory.EnumerateDirectories(dir))
            {
                var subdirName = subdir.AsMemory(dir.Length + 1).Span;

                AttendanceMode attendanceMode;
                if (subdirName.Equals("zi", StringComparison.OrdinalIgnoreCase))
                {
                    attendanceMode = AttendanceMode.Zi;
                }
                else if (subdirName.Equals("fr", StringComparison.OrdinalIgnoreCase))
                {
                    attendanceMode = AttendanceMode.FrecventaRedusa;
                }
                else
                {
                    throw new NotSupportedException($"Invalid attendance mode: {subdirName}");
                }
                yield return new(
                    attendanceMode: attendanceMode,
                    path: new LocalPath(subdir).ResolveToCurrentDirectory(),
                    studyYear: t.StudyYear,
                    semester: t.Sem);
            }
        }
    }

    public static IEnumerable<ScheduleDirectoryDescriptor> MatchingStudyYear(
        this IEnumerable<ScheduleDirectoryDescriptor> dirs,
        StudyYearOptions opts)
    {
        foreach (var dir in dirs)
        {
            if (dir.StudyYear != opts.StudyYear
                || dir.Semester != opts.Semester)
            {
                continue;
            }
            yield return dir;
        }
    }

    private static (int StudyYear, Semester Sem)? ParseFirst(ReadOnlyMemory<char> path)
    {
        var baseParser = new Parser(path);
        InvalidScheduleDirectoryFormat Error(ParserPosition position, string reason)
        {
            var segment = baseParser.Segment(position);
            return new(segment, reason);
        }

        var parser = baseParser.BufferedView();
        {
            // Only skip a path if it fails the first check.
            if (StudyYear() is not { } studyYear)
            {
                return null;
            }
            {
                bool skipped = parser.ConsumeExactChar('_');
                Debug.Assert(skipped);
            }
            var sem = Sem();
            return (studyYear, sem);
        }

        int? StudyYear()
        {
            var bparser = parser.BufferedView();
            var result = bparser.SkipUntilAny("_");
            if (!result.SkippedAny)
            {
                return null;
            }
            var yearSpan = parser.PeekSpanUntilPosition(bparser.Position);
            if (!int.TryParse(yearSpan, out int ret))
            {
                return null;
            }
            parser.MoveTo(bparser.Position);
            return ret;
        }

        Semester Sem()
        {
            var bparser = parser.BufferedView();
            if (!bparser.ConsumeExactString("sem"))
            {
                throw Error(bparser.Position, "Expected `sem`");
            }

            parser.MoveTo(bparser.Position);
            var semSpan = parser.PeekSpanUntilEnd();
            if (!int.TryParse(semSpan, out int sem))
            {
                throw Error(bparser.Position, "Expected a sem number.");
            }

            return SemesterHelper.FromInt(sem);
        }
    }
}

public sealed class ScheduleDirectoryDescriptor
{
    public AttendanceMode AttendanceMode { get; }
    public int StudyYear { get; }
    public Semester Semester { get; }
    public AbsolutePath Path { get; }

    public ScheduleDirectoryDescriptor(
        AttendanceMode attendanceMode,
        AbsolutePath path,
        int studyYear,
        Semester semester)
    {
        AttendanceMode = attendanceMode;
        Path = path;
        StudyYear = studyYear;
        Semester = semester;
    }

    public IScheduleLoaderComponent GetLoader()
    {
        switch (AttendanceMode)
        {
            case AttendanceMode.FrecventaRedusa:
            {
                return new FRScheduleDirectoryLoaderComponent
                {
                    DirectoryPath = Path,
                };
            }
            case AttendanceMode.Zi:
            {
                return new DirectoryScheduleLoaderComponent
                {
                    DirectoryPath = Path,
                };
            }
            default:
            {
                throw Unreachable();
            }
        }
    }
}

public sealed class InvalidScheduleDirectoryFormat : NotSupportedException
{
    public InvalidScheduleDirectoryFormat(ParserSegment segment, string expected)
        : base($"Invalid directory name format at `{segment}`. {expected}")
    {
        Segment = segment;
    }

    public ParserSegment Segment { get; }
}

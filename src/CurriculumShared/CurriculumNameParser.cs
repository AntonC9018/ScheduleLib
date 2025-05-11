using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib.Curriculum;

public sealed class CurriculumGroupKey
{
    public required QualificationType QualificationType { get; init; }
    public required AttendanceModeFlags AttendanceModes { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
}

public sealed class ParsedCurriculumKey1
{
    public required int Number;
    public required ReadOnlyMemory<char> Specialty;
    public required int Grade;
    public required ReadOnlyMemory<char> Course;
    public required List<ReadOnlyMemory<char>> Teachers;
    public required int Year;
}


public static class CurriculumNameParser
{
    public static CurriculumGroupKey ParseGroupKey(ref Parser parser)
    {
        var qual = Qualification(ref parser);
        parser.SkipWhitespace();
        var code = Code(ref parser);
        parser.SkipWhitespace();

        ParserPosition lastSegmentPos = default;
        ParserPosition spacePos = default;
        bool hadSpaces = false;
        {
            var bparser = parser.BufferedView();
            while (true)
            {
                var r = bparser.SkipNotWhitespace();
                if (r.EndOfInput)
                {
                    break;
                }
                hadSpaces = true;
                spacePos = bparser.Position;
                bparser.SkipWhitespace();
                lastSegmentPos = bparser.Position;
            }
        }

        var attendanceModes = AttendanceModeFlags.None;

        // process last segment
        if (hadSpaces)
        {
            var lastSegmentParser = parser.BufferedView();
            lastSegmentParser.MoveTo(lastSegmentPos);
            var lastSegment = lastSegmentParser.SourceUntilEnd();
            attendanceModes = AttendanceModes(lastSegment.Span);
        }

        ReadOnlyMemory<char> nameSegment;
        {
            var nameParser = parser.BufferedView();
            if (attendanceModes == AttendanceModeFlags.None)
            {
                nameSegment = nameParser.SourceUntilEnd();
            }
            else
            {
                nameSegment = nameParser.SourceUntilExclusive(spacePos);
            }
        }

        return new()
        {
            Code = code.ToString(),
            Name = nameSegment.ToString(),
            AttendanceModes = attendanceModes,
            QualificationType = qual,
        };

        static AttendanceModeFlags AttendanceModes(ReadOnlySpan<char> lastSpan)
        {
            var ret = AttendanceModeFlags.None;
            int itemCount = 0;
            bool hasUnparsedItem = false;
            foreach (var x in lastSpan.Split('+'))
            {
                var segment = lastSpan[x];
                if (segment.Length == 0)
                {
                    continue;
                }

                itemCount++;

                var maybeMode = GetMode(segment);
                if (maybeMode is not { } mode)
                {
                    if (hasUnparsedItem)
                    {
                        break;
                    }
                    hasUnparsedItem = true;
                    continue;
                }


                var flag = (AttendanceModeFlags) (1 << (int) mode);
                if ((ret & flag) == flag)
                {
                    throw new NotSupportedException("The same attendance mode specified a second time");
                }
                ret |= flag;
                continue;

                static AttendanceMode? GetMode(ReadOnlySpan<char> s)
                {
                    if (s.SequenceEqual("zi"))
                    {
                        return AttendanceMode.Zi;
                    }
                    if (s.SequenceEqual("fr"))
                    {
                        return AttendanceMode.FrecventaRedusa;
                    }
                    return null;
                }
            }

            if (hasUnparsedItem && itemCount > 1)
            {
                throw new NotSupportedException("'+' in the last segment without it parsing");
            }

            return ret;
        }

        static ReadOnlyMemory<char> Code(ref Parser parser)
        {
            var bparser = parser.BufferedView();

            var skipResult = bparser.SkipNumbers();
            if (!skipResult.SkippedAny)
            {
                throw new NotSupportedException("Expected code");
            }
            if (skipResult.EndOfInput)
            {
                throw new NotSupportedException("Expected '.' after first part of code");
            }
            if (bparser.Current == '.')
            {
                bparser.Move();

                var secondPartSkipResult = bparser.SkipNumbers();
                if (!secondPartSkipResult.SkippedAny)
                {
                    throw new NotSupportedException("Expected number after '.'");
                }
            }

            var ret = parser.SourceUntilExclusive(bparser);
            parser.MoveTo(bparser.Position);
            return ret;
        }

        static QualificationType Qualification(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            if (!bparser.SkipLetters().SkippedAny)
            {
                return QualificationType.Licenta;
            }

            var s = parser.PeekSpanUntilPosition(bparser.Position);
            parser.MoveTo(bparser.Position);

            if (IgnoreDiacriticsComparer.Instance.Equals(s, "master"))
            {
                return QualificationType.Master;
            }

            throw new NotSupportedException("Unknown qualification");
        }
    }

    public static ParsedCurriculumKey1? TryParseCurriculumKey(ref Parser parser)
    {
        if (Number(ref parser) is not { } num)
        {
            return null;
        }
        var specialty = Specialty(ref parser);
        var grade = Grade(ref parser);
        var course = Course(ref parser);
        var teachers = Teachers(ref parser);
        int year = Year(ref parser);

        return new()
        {
            Number = num,
            Specialty = specialty,
            Grade = grade,
            Course = course,
            Teachers = teachers,
            Year = year,
        };

        static ReadOnlyMemory<char> Course(ref Parser parser)
        {
            var error = "There must be the short course name following the year";
            var ret = NextSegmentUntilSep(ref parser, error);
            if (!parser.ConsumeExactString("_"))
            {
                throw new NotSupportedException("Expected _ after the course name.");
            }
            return ret;
        }

        static int Grade(ref Parser parser)
        {
            if (!parser.ConsumeExactString("an"))
            {
                throw new NotSupportedException("Expected 'anX' where X is the year after the specialty");
            }
            if (parser.IsEmpty || !char.IsDigit(parser.Current))
            {
                throw new NotSupportedException("A number must follow 'an'");
            }
            var grade = parser.Current - '0';
            parser.Move();

            if (!parser.ConsumeExactString("_"))
            {
                throw new NotSupportedException("Expected _ after the grade.");
            }
            return grade;
        }

        static List<ReadOnlyMemory<char>> Teachers(ref Parser parser)
        {
            var teachers = new List<ReadOnlyMemory<char>>();
            while (true)
            {
                var bparser = parser.BufferedView();
                var skipResult = bparser.SkipUntilAny(['_']);
                if (skipResult.EndOfInput)
                {
                    break;
                }

                var name = parser.SourceUntilExclusive(bparser.Position);
                teachers.Add(name);

                bparser.Move();
                parser.MoveTo(bparser.Position);
            }
            return teachers;
        }

        static int Year(ref Parser parser)
        {
            var consumeIntResult = parser.ConsumePositiveInt(length: 4);
            if (consumeIntResult.Status != ConsumeIntStatus.Ok)
            {
                throw new NotSupportedException("Expecting year at the end.");
            }
            return (int) consumeIntResult.Value;
        }

        static int? Number(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            var numSkipResult = bparser.SkipNumbers();
            if (!numSkipResult.SkippedAny)
            {
                return null;
            }

            var nums = parser.PeekSpanUntilPosition(bparser.Position);
            if (!int.TryParse(nums, out int ret))
            {
                throw new NotSupportedException("Number in front too large");
            }

            parser.MoveTo(parser.Position);
            if (!parser.ConsumeExactString("_"))
            {
                return null;
            }

            return ret;
        }

        static ReadOnlyMemory<char> Specialty(ref Parser parser)
        {
            var error = "There must be the short specialty name following the initial numbers";
            var ret = NextSegmentUntilSep(ref parser, error);
            if (!parser.ConsumeExactString("_"))
            {
                throw new NotSupportedException("Expected _ after the specialty.");
            }
            return ret;
        }

        static ReadOnlyMemory<char> NextSegmentUntilSep(ref Parser parser, string error)
        {
            var bparser = parser.BufferedView();
            var result = bparser.SkipUntilAny(['_']);
            if (!result.SkippedAny)
            {
                throw new NotSupportedException(error);
            }

            var ret = parser.SourceUntilExclusive(bparser.Position);
            return ret;
        }
    }

}

public sealed class FindCurriculumForLessonParams
{
    public required RegularLessonId LessonId { get; init; }
    public required Schedule Schedule { get; init; }
    public required LookupFacade Lookup { get; init; }
}

public sealed class Curriculum
{
}

// For type safety.
public readonly record struct RootCurriculumDirectory(string FullPath);
public readonly record struct CurriculumGroupDirectory(string FullPath);
public readonly record struct CurriculumGroup(CurriculumGroupKey Key, CurriculumGroupDirectory Directory);
public readonly record struct CurriculumFile(ParsedCurriculumKey1 Key, string FilePath);

public static class CurriculumDirectoryHelper
{
    public const string DefaultRootDirName = "curricula";

    public static IEnumerable<CurriculumGroup> ListGroups(this RootCurriculumDirectory directory)
    {
        var dirs = Directory.EnumerateDirectories(directory.FullPath);
        foreach (var dirFullPath in dirs)
        {
            var lastSegmentStart = dirFullPath.IndexOf(Path.DirectorySeparatorChar) + 1;
            var parser = new Parser(dirFullPath);
            parser.Move(lastSegmentStart);
            var groupKey = CurriculumNameParser.ParseGroupKey(ref parser);
            if (!parser.IsEmpty)
            {
                throw new InvalidOperationException("Directory name not parsed fully.");
            }

            var d = new CurriculumGroupDirectory(dirFullPath);
            yield return new(groupKey, d);
        }
    }

    public static IEnumerable<CurriculumFile> ListFiles(this CurriculumGroupDirectory directory)
    {
        var dirs = Directory.EnumerateDirectories(directory.FullPath);
        foreach (var fileFullPath in dirs)
        {
            const string extension = ".docx";
            if (!fileFullPath.EndsWith(extension))
            {
                continue;
            }
            var lastSegmentStart = fileFullPath.IndexOf(Path.DirectorySeparatorChar) + 1;
            var parser = new Parser(fileFullPath);
            parser.Move(lastSegmentStart);
            var key = CurriculumNameParser.TryParseCurriculumKey(ref parser);
            if (key is null)
            {
                continue;
            }
            if (parser.ConsumeExactString(extension))
            {
                throw new InvalidOperationException("Directory name not parsed fully.");
            }

            yield return new(key, fileFullPath);
        }
    }
}

public sealed class CurriculumCache
{
    private readonly RootCurriculumDirectory RootDirectory;

    public CurriculumCache(
        RootCurriculumDirectory rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public static CurriculumCache CreateDefault()
    {
        var rootPath = Path.GetFullPath(CurriculumDirectoryHelper.DefaultRootDirName);
        var ret = new CurriculumCache(new(rootPath));
        return ret;
    }

    private CurriculumGroup? FindGroup(FindCurriculumForLessonParams p)
    {
        foreach (var group in RootDirectory.ListGroups())
        {
            var lesson = p.Schedule.Get(p.LessonId);
            if (!AttendanceModeOk())
            {
                continue;
            }
            if (!QualificationOk())
            {
                continue;
            }
            if (!NameOk())
            {
                continue;
            }

            return group;

            bool NameOk()
            {
                // TODO: the same thing we have for course but for specialty
                var g = p.Schedule.Get(lesson.Lesson.Group);
                var facultyInitialsParsed = new ParsedCourseName();
                foreach (var letter in g.Faculty.Name)
                {
                    facultyInitialsParsed.Segments.Add(
                        CourseNameSegment.AsInitials(letter));
                }
                var facultyParsed = CourseNameParsing.Parse(new(new()), group.Key.Name, new()
                {
                    IgnorePunctuation = false,
                });

                if (!facultyParsed.Equals(facultyInitialsParsed))
                {
                    return false;
                }
                return true;
            }

            bool QualificationOk()
            {
                var q = group.Key.QualificationType;
                var g = p.Schedule.Get(lesson.Lesson.Group);
                if (g.QualificationType != q)
                {
                    return false;
                }
                return true;
            }

            bool AttendanceModeOk()
            {
                var m = group.Key.AttendanceModes;
                if (m == AttendanceModeFlags.None)
                {
                    return true;
                }
                var g = p.Schedule.Get(lesson.Lesson.Group);
                if (m.Has(g.AttendanceMode))
                {
                    return true;
                }
                return false;
            }
        }
        return null;
    }

    public Curriculum? FindCurriculumForCourse(FindCurriculumForLessonParams p)
    {
        if (FindGroup(p) is not { } group)
        {
            return null;
        }


        List<CurriculumFile> candidateFiles = new();
        foreach (var file in group.Directory.ListFiles())
        {
            if (!CourseOk())
            {
                continue;
            }
            if (!GradeOk())
            {
                continue;
            }
            candidateFiles.Add(file);
            continue;

            bool CourseOk()
            {
                if (p.Lookup.Course(file.Key.Course.Span) is not { } courseId)
                {
                    return false;
                }

                var lesson = p.Schedule.Get(p.LessonId);
                if (courseId != lesson.Lesson.Course)
                {
                    return false;
                }

                return true;
            }

            bool GradeOk()
            {
                var lesson = p.Schedule.Get(p.LessonId);
                var g = p.Schedule.Get(lesson.Lesson.Group);
                if (g.Grade.Value != file.Key.Grade)
                {
                    return false;
                }
                return true;
            }
        }

        if (candidateFiles.Count > 1)
        {
            // Keep teacher overlap, see if that nail it down.
            var withTeacherOverlap = candidateFiles.Where(x =>
            {
                foreach (var teacher in x.Key.Teachers)
                {
                    if (p.Lookup.Teacher(teacher) is not { } teacherId)
                    {
                        // throw new InvalidOperationException("Teacher not found!");
                        continue;
                    }

                    var lesson = p.Schedule.Get(p.LessonId);
                    if (lesson.Lesson.Teachers.Contains(teacherId))
                    {
                        return true;
                    }
                }
                return false;
            }).ToList();

            if (withTeacherOverlap.Count == 1)
            {
                candidateFiles = withTeacherOverlap;
            }
        }

        if (candidateFiles.Count > 1)
        {
            // Read the files and narrow it down by inspecting the file.
            // Unimplemented for now.
        }

        if (candidateFiles.Count != 1)
        {
            throw new InvalidOperationException("Could not narrow down the curriculum.");
        }


    }
}

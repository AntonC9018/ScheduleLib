using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib.Curriculum;
using NameModel = TeacherBuilderModel.NameModel;

public sealed class CurriculumGroupKey
{
    public required QualificationType QualificationType { get; init; }
    public required AttendanceModeFlags AttendanceModes { get; init; }
    public required ProgramCode Code { get; init; }
    public required SpecializationName Name { get; init; }
}

public readonly record struct ProgramCode(string Value);
public readonly record struct SpecializationName(string Value);

public sealed class ParsedCurriculumKey1
{
    public required int Number;
    public required ReadOnlyMemory<char> Specialty;
    public required int Grade;
    public required ReadOnlyMemory<char> Course;
    public required List<ReadOnlyMemory<char>> Teachers;
    public required int Year;
}

public sealed class Curriculum
{
    public required List<NameModel> AuthorNames;
    public required string PreliminaryPassage;
    public required DisciplineProvisions DisciplineProvisions;
}

public sealed class DisciplineProvisions
{
    public required List<DisciplineProvision> Provisions;
}

public sealed class DisciplineProvision
{
    public required string CourseName;
    public required AttendanceMode AttendanceMode;
    public required DisciplineCode DisciplineCode;
    public required UnsizedBitArray32 TeachersMask;
    public required Semester Semester;
    public required DisciplineTimeDistribution TimeDistribution;
    public required EvaluationMode EvaluationMode;
    public required Credits Credits;
}

public sealed class DisciplineTimeDistribution
{
    public required int Total;
    public required int Course;
    public required int Seminar;
    public required int Lab;
    public required int IndividualWork;
}

public readonly record struct DisciplineCode(string Value);
public readonly record struct Semester(int Number);
public readonly record struct EvaluationMode(bool HasExam);
public readonly record struct Credits(int Value);


public sealed class AllLessonPlan
{
    public required List<LessonUnitPlan> Units;
}

public sealed class LessonUnitPlan
{
    public required string Name;
    public required List<LessonPlan> Lessons;
}

public sealed class LessonPlan
{
    public required string Name;
    public required OneForEachAttendanceMode<LessonPlanTimeDistribution?> TimeDistributions;
}

public sealed class LessonPlanTimeDistribution
{
    public required int Course;
    public required int Lab;
    public required int IndividualWork;
}

public sealed class CompetenceCollection
{
    public required List<Competence> Competences;
}

public sealed class Competence
{
    public required string Category;
    public required CompetenceId Id;
    public required string Name;
    public required string Description;
}

public readonly record struct CompetenceId(string Value);

public static class CurriculumNameParser
{
    public static CurriculumGroupKey ParseGroupKey(ref Parser parser)
    {
        var qual = Qualification(ref parser);
        parser.SkipWhitespace();
        var code = ParseProgramCode(ref parser);
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
            Code = new(code.ToString()),
            Name = new(nameSegment.ToString()),
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


        static QualificationType Qualification(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            if (!bparser.SkipLetters().SkippedAny)
            {
                return QualificationType.Licenta;
            }

            var s = parser.PeekSpanUntilPosition(bparser.Position);
            parser.MoveTo(bparser.Position);

            if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(s, "master"))
            {
                return QualificationType.Master;
            }

            throw new NotSupportedException("Unknown qualification");
        }
    }

    public static ReadOnlyMemory<char> ParseProgramCode(ref Parser parser)
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
                var facultyParsed = CourseNameParsing.Parse(new(new()), group.Key.Name.Value, new()
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

    public async Task<Curriculum?> FindCurriculumForCourse(FindCurriculumForLessonParams p)
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

        var ret = await ReadFile(candidateFiles[0]);
        return ret;
    }

    private static async Task<Curriculum> ReadFile(CurriculumFile file)
    {
        await using var fileStream = File.OpenRead(file.FilePath);
        using var word = WordprocessingDocument.Open(fileStream, isEditable: false, new()
        {
        });

        // CURRICULUM
        // la unitatea de curs / modulul
        // Rețele de calculatoare
        // Ciclul I, Licență
        // Program / Specialitatea: 0613.4 Informatică, 0613.5 Informatică Aplicată
        if (word.MainDocumentPart?.Document is not { } document)
        {
            throw new InvalidOperationException("No document found.");
        }
        if (document.Body is not { } body)
        {
            throw new InvalidOperationException("No body found.");
        }

        IEnumerable<Paragraph> FirstPageParagraphs()
        {
            var elems = body.Descendants();
            using var elemsE = elems.GetEnumerator();
            while (true)
            {
                if (!elemsE.MoveNext())
                {
                    yield break;
                }
                if (elemsE.Current is Break)
                {
                    yield break;
                }
                if (elemsE.Current is Paragraph p)
                {
                    yield return p;
                }
            }
        }

        using var paragraphs = FirstPageParagraphs().GetEnumerator();
        if (!SkipUntilCurriculum())
        {
            throw new InvalidOperationException("Document does not contain CURRICULUM");
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("No paragraphs found after CURRICULUM");
        }

        {
            var t = paragraphs.Current.InnerText;
            const string expected = "la unitatea de curs / modulul";
            if (t != expected)
            {
                throw new InvalidOperationException($"Expected string {expected}");
            }
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("Expected the course name to follow");
        }

        string realFullCourseName;
        {
            realFullCourseName = paragraphs.Current.InnerText;
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("Expected the year and qualification to follow");
        }

        YearAndQualificationType yearAndQualificationType;
        {
            var t = paragraphs.Current.InnerText;
            yearAndQualificationType = ParseYearAndQualificationType(t);
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("Expected the program & specialty after year");
        }

        Program program;
        {
            var t = paragraphs.Current.InnerText;
            program = ParseProgram(t);
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("Expected author after the program");
        }

        {
            var t = paragraphs.Current.InnerText;
            var parser = new Parser(t);
            if (!parser.ConsumeExactString("autor:", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected AUTOR:");
            }
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("Expected author name paragraph");
        }

        List<NameModel> authorNames = new();
        while (true)
        {
            var p = paragraphs.Current;
            if (p.ChildElements.Count != 2)
            {
                if (authorNames.Count == 0)
                {
                    throw new InvalidOperationException("No author name specified");
                }
                break;
            }

            var qualText = p.ChildElements[0];
            var t = qualText.InnerText;
            ValidateQualText(t);

            var nameText = p.ChildElements[1];
            {
                var name = nameText.InnerText;
                var teacherName = TeacherNameHelper.ParseName(name);
                var authorName = teacherName;
                authorNames.Add(authorName);
            }
        }

        // Extract the year from Chișinău 2024 in the middle.
        Paragraph? lastParagraph = null;
        {
            while (paragraphs.MoveNext())
            {
                lastParagraph = paragraphs.Current;
            }
            if (lastParagraph is null)
            {
                throw new InvalidCastException("Expected more stuff after the author");
            }
        }
        uint year;
        {
            var t = lastParagraph.InnerText;
            var parser = new Parser(t);
            // TODO: Support ignore diacritics (annoying)
            if (!parser.ConsumeExactString("Chișinău", StringComparison.CurrentCultureIgnoreCase))
            {
                throw new InvalidOperationException("Expected the city-year line as the last line");
            }

            if (!parser.SkipWhitespace().SkippedAny)
            {
                throw new InvalidOperationException("Expected year after city on the last line");
            }

            var yearResult = parser.ConsumePositiveInt(length: 4);
            if (yearResult.Status != ConsumeIntStatus.Ok)
            {
                throw new InvalidOperationException("The year must be 4 digits");
            }

            year = yearResult.Value;
        }

        return new Curriculum
        {
            AuthorNames = authorNames,
            DisciplineProvisions = null!,
            PreliminaryPassage = "",
        };

        static Program ParseProgram(string t)
        {
            var parser = new Parser(t);
            {
                const string programPrefix = "Program / Specialitatea:";
                if (!parser.ConsumeExactString(programPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Expected string {programPrefix}");
                }
            }

            parser.SkipWhitespace();

            ReadOnlyMemory<char> code;
            try
            {
                code = CurriculumNameParser.ParseProgramCode(ref parser);
            }
            catch (NotSupportedException e)
            {
                throw new InvalidOperationException($"Error while parsing program code: {e}", e);
            }

            var specialty = parser.SourceUntilEnd();
            return new()
            {
                Code = new(code.ToString()),
                Name = new(specialty.ToString()),
            };
        }

        static YearAndQualificationType ParseYearAndQualificationType(string t)
        {
            var parser = new Parser(t);
            int year = ParseYear(ref parser);

            if (!parser.ConsumeExactString(","))
            {
                throw new InvalidOperationException("Expected the qualification type after the year");
            }
            if (!parser.SkipWhitespace().SkippedAny)
            {
                throw new InvalidOperationException("Expected the qualification type after the year");
            }

            var qualificationType = ParseQualificationType(ref parser);

            return new(year, qualificationType);
        }

        static int ParseYear(ref Parser parser)
        {
            const string ciclul = "Ciclul";
            {
                if (!parser.ConsumeExactString(ciclul, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Expected the year string to start with '{ciclul}'");
                }
            }
            if (!parser.SkipWhitespace().SkippedAny)
            {
                throw new InvalidOperationException($"Did not expect the string to end after '{ciclul}'");
            }

            {
                var romanReadStatus = parser.ReadRoman();
                if (romanReadStatus.Status != ReadRomanStatus.Ok)
                {
                    throw new InvalidOperationException($"Roman number must follow after '{ciclul}'");
                }

                return romanReadStatus.Number;
            }
        }

        static QualificationType ParseQualificationType(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            var r = bparser.SkipNotWhitespace();
            if (!r.EndOfInput)
            {
                throw new InvalidOperationException("Expected only one qualification type");
            }

            var name = parser.PeekSpanUntilPosition(bparser.Position);
            var qualificationType = GetQualificationType(name);
            return qualificationType;

            static QualificationType GetQualificationType(ReadOnlySpan<char> name)
            {
                if (Equals1(name, "licenta"))
                {
                    return QualificationType.Licenta;
                }
                else if (Equals1(name, "master"))
                {
                    return QualificationType.Master;
                }
                else
                {
                    throw new InvalidOperationException("Unknown qualification type");
                }
            }

            static bool Equals1(ReadOnlySpan<char> name, ReadOnlySpan<char> label)
            {
                if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(name, label))
                {
                    return true;
                }
                return false;
            }
        }

        static void ValidateQualText(string t)
        {
            ShortenedWord[] exactShortTokens = [
                new("dr"),
            ];
            Word[] allowedTokens = [
                new("doctor"),
                new("conferențiar"),
                new("asistent"),
            ];
            Word[] ignoredTokens = [
                new("asistent"),
            ];
            var span = t.AsSpan();
            foreach (var range in span.Split(' '))
            {
                var part = span[range];
                if (part.Length == 0)
                {
                    continue;
                }

                if (!ValidateWord(part))
                {
                    throw new InvalidOperationException($"Disallowed teacher qualification token: {part}");
                }
            }
            return;


            bool ValidateWord(ReadOnlySpan<char> part)
            {
                var partWord = new WordSpan(part);
                foreach (var ignoredToken in ignoredTokens)
                {
                    if (ignoredToken.Span.IsEqual(partWord))
                    {
                        return true;
                    }
                }
                if (!partWord.LooksFull)
                {
                    foreach (var shortToken in exactShortTokens)
                    {
                        if (shortToken.Span.Value.Equals(
                                partWord.Value,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                foreach (var allowedToken in allowedTokens)
                {
                    if (allowedToken.Span.IsEqual(partWord))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        bool SkipUntilCurriculum()
        {
            while (paragraphs.MoveNext())
            {
                var t = paragraphs.Current.InnerText;
                if (t.Equals("curriculum", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

}

public readonly record struct YearAndQualificationType(int Year, QualificationType QualificationType);

public readonly record struct Program(
    ProgramCode Code,
    SpecializationName Name);

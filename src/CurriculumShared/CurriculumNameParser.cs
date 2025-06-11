using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml;
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

public sealed class CurriculumGeneralInfo
{
    public required string CourseName;
    public required List<NameModel> AuthorNames;
    public required uint Year;
    public required YearAndQualificationType YearAndQual;
    public required List<Program> Programs;
}

public sealed class Curriculum
{
    public required CurriculumGeneralInfo General;
    public required Preliminary? Preliminary;
    public required AllDisciplineProvisions DisciplineProvisions;
    public required AllLessonPlan Lessons;
    public required AllLabsPlan Labs;
    public required AllStudiedUnits StudiedUnits;
    public required CompetenceCollection Competences;
}

internal struct ParsingState()
{
    public SectionType? CurrentSectionType = null;
    public UnsizedBitArray32 ProcessedSections = new();
    public CurriculumInParsing Curriculum = new();

    // Used internally.
    public OpenXmlElementList.Enumerator SectionContentStart = default;

    public readonly StringBuilder Accumulator = new();
}

internal struct CurriculumInParsing()
{
    public Preliminary? Preliminary = null;
    public List<DisciplineProvision> DisciplineProvisions = new();
    public List<LessonPlan> Lessons = new();
    public List<LabPlan> Labs = new();
    public List<StudiedUnit> StudiedUnits = new();
    public List<Competence> Values = new();
}

internal struct PreliminaryState()
{
    public PreliminaryField Field = PreliminaryField.None;
    public UnsizedBitArray32 Processed = default;
    public Preliminary Preliminary = new();
}
public enum PreliminaryField
{
    None = -1,
    Overview,
    Importance,
    Languages,
    Beneficiaries,
    Count,
}

public sealed class Preliminary
{
    public string? Overview;
    public string? Importance;
    public string? Languages;
    public string? Beneficiaries;

    public void Set(PreliminaryField field, string str)
    {
        switch (field)
        {
            case PreliminaryField.Overview:
                Overview = str;
                break;
            case PreliminaryField.Importance:
                Importance = str;
                break;
            case PreliminaryField.Languages:
                Languages = str;
                break;
            case PreliminaryField.Beneficiaries:
                Beneficiaries = str;
                break;
            default:
                throw new NotSupportedException($"Unknown preliminary field {field}");
        }
    }
}

public sealed class AllDisciplineProvisions
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
    public required List<Competence> Values;
}

public sealed class Competence
{
    public required string Category;
    public required CompetenceId Id;
    public required string Name;
    public required string Description;
}

public readonly record struct CompetenceId(string Value);


public sealed class AllStudiedUnits
{
    public required List<StudiedUnit> Units;
}

public sealed class StudiedUnit
{
    public required string Topic;
    public required List<CompetenceId> Competences;
    public required List<KeyTerm> KeyTerms;
    public required List<ContentUnit> ContentUnits; // the dot-separated list
    public required List<LearnedAbility> LearnedAbilities;
    public required List<StudentResponsibility> StudentResponsibilities;
}

public readonly record struct ContentUnit(string Value);
public readonly record struct KeyTerm(string Value);
public readonly record struct LearnedAbility(string Value);
public readonly record struct StudentResponsibility(string Value);


public sealed class AllLabsPlan
{
    public required List<LabPlan> Labs;
}

public sealed class LabPlan
{
    public required string Name;
    public required string Description;
    public required string? EvaluationCriteria;
    public required Term Term;
}

public readonly record struct Term(StartOrEnd At, int Week);

public enum StartOrEnd
{
    Start,
    End,
}

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

            parser.MoveTo(bparser.Position);
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
            parser.MoveTo(bparser.Position);
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
public readonly record struct CurriculumFile(ParsedCurriculumKey1 Key, string FilePath)
{
    public static CurriculumFile? FromFilePath(string filePath)
    {
        var r = CurriculumDirectoryHelper.ParseCurriculumFileKey(filePath);
        if (r is null)
        {
            return null;
        }
        return new(r, filePath);
    }
}

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

    private const string CurriculumDocumentExtension = ".docx";

    internal static ParsedCurriculumKey1? ParseCurriculumFileKey(string filePath)
    {
        Debug.Assert(filePath.EndsWith(CurriculumDocumentExtension));
        var lastSegmentStart = filePath.IndexOf(Path.DirectorySeparatorChar) + 1;
        var parser = new Parser(filePath);
        parser.Move(lastSegmentStart);
        var key = CurriculumNameParser.TryParseCurriculumKey(ref parser);
        if (key is null)
        {
            return null;
        }
        if (!parser.ConsumeExactString(CurriculumDocumentExtension))
        {
            throw new ArgumentException(
                message: "Curriculum file name not parsed fully",
                paramName: nameof(filePath));
        }
        return key;
    }

    public static IEnumerable<CurriculumFile> ListFiles(this CurriculumGroupDirectory directory)
    {
        var dirs = Directory.EnumerateFiles(
            path: directory.FullPath,
            searchPattern: $"*{CurriculumDocumentExtension}");
        foreach (var fileFullPath in dirs)
        {
            var key = ParseCurriculumFileKey(fileFullPath);
            if (key == null)
            {
                continue;
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
                    if (p.Lookup.Teacher(teacher.Span) is not { } teacherId)
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

    internal static async Task<Curriculum> ReadFile(CurriculumFile file)
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

        // ReSharper disable once GenericEnumeratorNotDisposed
        using var source = body.ChildElements.GetEnumerator().Wrap(default(OpenXmlElement));

        CurriculumGeneralInfo general;
        {
            using var paragraphs = FirstSectionParagraphs(source);
            general = ParseGeneralInfo(paragraphs);
        }

        var parsingState = new ParsingState();

        static void ProcessSection(
            ref ParsingState parsingState,
            SectionItemsEnumerable items)
        {
            switch (parsingState.CurrentSectionType)
            {
                case SectionType.PreliminaryPassage:
                {
                    parsingState.Curriculum.Preliminary = PreliminarySectionProcessing.Process(ref parsingState, items);
                    break;
                }
            }
        }

        static void SetCurrentSection(
            ref ParsingState parsingState,
            SectionType newSectionType)
        {
            if (parsingState.ProcessedSections.IsSet((int) newSectionType))
            {
                throw new NotSupportedException("The same section appears twice, which is not allowed");
            }
            parsingState.CurrentSectionType = newSectionType;
            parsingState.ProcessedSections.Set((int) newSectionType);
        }

        while (source.MoveNext())
        {
            var currentItem = source.Current;
            if (CheckNewSection(currentItem) is not { } section)
            {
                continue;
            }
            MaybeProcessCurrentSection(ref parsingState, currentItem);
            SetCurrentSection(ref parsingState, section);
            parsingState.SectionContentStart = source.EnumeratorState;
        }
        MaybeProcessCurrentSection(ref parsingState, null);

        return new Curriculum
        {
            General = general,
            DisciplineProvisions = null!,
            Preliminary = parsingState.Curriculum.Preliminary,
            Competences = new()
            {
                Values = new(),
            },
            Labs = new()
            {
                Labs = new(),
            },
            Lessons = new()
            {
                Units = new(),
            },
            StudiedUnits = new()
            {
                Units = new(),
            },
        };

        static void MaybeProcessCurrentSection(
            ref ParsingState state,
            OpenXmlElement? currentItem)
        {
            if (state.CurrentSectionType == null)
            {
                return;
            }

            var children = new SectionItemsEnumerable(state.SectionContentStart, currentItem);
            ProcessSection(ref state, children);
            state.CurrentSectionType = null;
        }

        static SectionType? CheckNewSection(OpenXmlElement currentChild)
        {
            var sectionResult = MaybeParseSectionType(currentChild);
            if (!sectionResult.IsSection)
            {
                return null;
            }
            var section = (SectionType) sectionResult.SectionType;
            // Console.WriteLine($"Found section type {section}");
            return section;
        }
    }

    private static readonly ImmutableArray<string> SectionStrings = CreateSectionStrings();
    private static ImmutableArray<string> CreateSectionStrings()
    {
        OneForEachSectionType<string> sections = new()
        {
            Bibliography = "bibliografie recomandata",
            Competences = "competente generale, profesionale si rezultatele invatarii",
            Labs = "lucrul individual al studentului",
            Suggestions = "sugestii metodologice de predare-invatare-evaluare",
            DisciplineProvisions = "administrarea disciplinei",
            LessonPlans = "tematica si repartizarea orientativa a orelor",
            PreliminaryPassage = "preliminarii",
            StudyUnits = "unitati de invatare",
        };

        var e = sections.GetEnumerator();
        var ret = ImmutableArray.CreateBuilder<string>(sections.Count());
        while (e.MoveNext())
        {
            ret.Add(e.Current);
        }
        return ret.MoveToImmutable();
    }

    private struct PreprocessIgnoreRomanOnce() : IPreprocess
    {
        private bool _skipRoman = false;

        public void Preprocess(ref Parser parser)
        {
            if (_skipRoman)
            {
                return;
            }

            _skipRoman = true;
            // Some of them might have a roman numeral in front. Skip it.
            if (parser.ReadRoman().Status == ReadRomanStatus.Ok)
            {
                // The dot is optional.
                parser.ConsumeExactString(".");
                parser.SkipWhitespace();
            }
        }
    }

    private static SectionParseResult MaybeParseSectionType(
        OpenXmlElement current)
    {
        if (current is not Paragraph para)
        {
            return SectionParseResult.CreateNotHeading();
        }
        if (!IsHeading())
        {
            return SectionParseResult.CreateNotHeading();
        }

        var ret = SectionParser.Parse(para, new PreprocessIgnoreRomanOnce(), SectionStrings);
        SectionParser.DefaultHandleError(ret);
        return ret;

        bool IsHeading()
        {
            foreach (var textItem in para.Descendants<Text>())
            {
                if (IsTextBoldAndUppercase(textItem))
                {
                    return true;
                }
            }

            // If there's a style indicator, check if it's a heading.
            // TODO: No precedents yet, idk if this actually works.
            // NOTE: Cannot be first, since some of them have a ListParagraph style.
            var isHeadingStyle = IsHeadingStyle();
            switch (isHeadingStyle)
            {
                case IsHeadingStyleResult.NotHeading:
                    return false;
                case IsHeadingStyleResult.Heading:
                    return true;
            }

            return false;
        }

        static bool IsTextBoldAndUppercase(Text text)
        {
            if (text.Parent is not Run run)
            {
                return false;
            }
            if (run.GetFirstChild<RunProperties>() is not { } props)
            {
                return false;
            }
            if (props.Bold is not { })
            {
                return false;
            }
            if (props.Caps is { })
            {
                return true;
            }

            // Manually check if each letter is uppercase.
            var textContent = text.Text;
            foreach (var c in textContent.AsSpan())
            {
                if (char.IsLetter(c) && !char.IsUpper(c))
                {
                    return false;
                }
            }

            return true;
        }

        IsHeadingStyleResult IsHeadingStyle()
        {
            var props = para.GetFirstChild<ParagraphProperties>();
            if (props is null)
            {
                return IsHeadingStyleResult.NoStyle;
            }
            if (props.GetFirstChild<ParagraphStyleId>() is not { } styleId)
            {
                return IsHeadingStyleResult.NoStyle;
            }
            if (styleId.Val is not { } styleIdVal)
            {
                return IsHeadingStyleResult.NotHeading;
            }
            if (styleIdVal.Value is not { } styleIdString)
            {
                return IsHeadingStyleResult.NotHeading;
            }
            if (!styleIdString.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
            {
                return IsHeadingStyleResult.NotHeading;
            }
            return IsHeadingStyleResult.Heading;
        }
    }

    private static CurriculumGeneralInfo ParseGeneralInfo(IEnumerator<Paragraph> paragraphs)
    {
        if (!SkipUntilCurriculum())
        {
            throw new InvalidOperationException("Document does not contain CURRICULUM");
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("No paragraphs found after CURRICULUM");
        }

        {
            var t = paragraphs.Current!.InnerText;
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

        List<Program> programs;
        {
            var t = paragraphs.Current.InnerText;
            programs = ParseProgram(t);
        }

        if (!paragraphs.MoveNext())
        {
            throw new InvalidOperationException("Expected author after the program");
        }

        while (true)
        {
            var t = paragraphs.Current.InnerText;
            var parser = new Parser(t);
            if (parser.ConsumeExactString("autor:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (!paragraphs.MoveNext())
            {
                throw new InvalidOperationException("Expected AUTOR:");
            }
        }

        List<NameModel> authorNames = new();
        while (true)
        {
            if (!paragraphs.MoveNext())
            {
                throw new InvalidOperationException("Expected author name paragraph");
            }

            var p = paragraphs.Current;
            if (p.ChildElements.OfType<Run>().MaybeJustTwoItems() is not { } runs)
            {
                if (authorNames.Count == 0)
                {
                    throw new InvalidOperationException("No author name specified");
                }
                break;
            }

            var qualText = runs.First;
            var t = qualText.InnerText;
            ValidateQualText(t);

            var nameText = runs.Second;
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

        return new()
        {
            CourseName = realFullCourseName,
            Programs = programs,
            Year = year,
            YearAndQual = yearAndQualificationType,
            AuthorNames = authorNames,
        };

        static List<Program> ParseProgram(string t)
        {
            var parser = new Parser(t);
            {
                const string programPrefix = "Program / Specialitatea:";
                if (!parser.ConsumeExactString(programPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Expected string {programPrefix}");
                }
            }

            var ret = new List<Program>();
            while (true)
            {
                {
                    var r = parser.SkipWhitespace();
                    if (!r.SkippedAny)
                    {
                        throw new InvalidOperationException("Expected whitespace around programs");
                    }
                }

                ReadOnlyMemory<char> code;
                try
                {
                    code = CurriculumNameParser.ParseProgramCode(ref parser);
                }
                catch (NotSupportedException e)
                {
                    throw new InvalidOperationException($"Error while parsing program code: {e}", e);
                }

                parser.SkipWhitespace();

                var bparser = parser.BufferedView();
                var nextResult = bparser.SkipUntilAny([',']);
                if (!nextResult.SkippedAny)
                {
                    throw new InvalidOperationException("Expected specialty name after the code");
                }

                var specialty = parser.SourceUntilExclusive(bparser.Position);
                ret.Add(new()
                {
                    Code = new(code.ToString()),
                    Name = new(specialty.ToString()),
                });

                if (nextResult.EndOfInput)
                {
                    return ret;
                }

                parser.MovePast(bparser.Position);
            }
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
                new("universitar"),
            ];
            var span = t.AsSpan();
            foreach (var range in span.SplitAny(" ,;"))
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
                    var short_ = partWord.Shortened.Value;
                    foreach (var shortToken in exactShortTokens)
                    {
                        bool equal = shortToken.Span.Value.Equals(
                            short_,
                            StringComparison.OrdinalIgnoreCase);
                        if (equal)
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
                var t = paragraphs.Current!.InnerText;
                if (t.Equals("curriculum", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    private static IEnumerator<Paragraph> FirstSectionParagraphs(IEnumerator<OpenXmlElement> source)
    {
        while (true)
        {
            if (!source.MoveNext())
            {
                yield break;
            }
            var c = source.Current;

            bool hadNestedParagraphs = false;
            bool hadBreak = false;
            foreach (var d in c.Descendants())
            {
                if (d is Break)
                {
                    hadBreak = true;
                    break;
                }
                if (d is not Paragraph p)
                {
                    continue;
                }
                hadNestedParagraphs = true;
                yield return p;
            }

            if (!hadNestedParagraphs)
            {
                if (c is Paragraph para)
                {
                    yield return para;
                }
            }
            if (hadBreak)
            {
                yield break;
            }
        }
    }
}

public readonly record struct YearAndQualificationType(
    int Year,
    QualificationType QualificationType);

public readonly record struct Program(
    ProgramCode Code,
    SpecializationName Name);

internal enum SectionType
{
    Unknown = -1,
    PreliminaryPassage,
    DisciplineProvisions,
    LessonPlans,
    Competences,
    StudyUnits,
    Labs,
    Suggestions,
    Bibliography,
    Count,
}

internal record struct OneForEachSectionType<T>
{
    public required T PreliminaryPassage;
    public required T DisciplineProvisions;
    public required T LessonPlans;
    public required T Competences;
    public required T StudyUnits;
    public required T Labs;
    public required T Suggestions;
    public required T Bibliography;
}

internal static class SectionTypeHelper
{
    public static int Count<T>(this in OneForEachSectionType<T> _) => (int) SectionType.Count;

    public static ref T Ref<T>(this ref OneForEachSectionType<T> item, SectionType p)
    {
        switch (p)
        {
            case SectionType.PreliminaryPassage:
                return ref item.PreliminaryPassage;
            case SectionType.DisciplineProvisions:
                return ref item.DisciplineProvisions;
            case SectionType.LessonPlans:
                return ref item.LessonPlans;
            case SectionType.Competences:
                return ref item.Competences;
            case SectionType.StudyUnits:
                return ref item.StudyUnits;
            case SectionType.Labs:
                return ref item.Labs;
            case SectionType.Suggestions:
                return ref item.Suggestions;
            case SectionType.Bibliography:
                return ref item.Bibliography;
            default:
                throw new ArgumentOutOfRangeException(nameof(p), p, "Unknown section type");
        }
    }

    public static T Get<T>(this in OneForEachSectionType<T> item, SectionType p)
    {
        switch (p)
        {
            case SectionType.PreliminaryPassage:
                return item.PreliminaryPassage;
            case SectionType.DisciplineProvisions:
                return item.DisciplineProvisions;
            case SectionType.LessonPlans:
                return item.LessonPlans;
            case SectionType.Competences:
                return item.Competences;
            case SectionType.StudyUnits:
                return item.StudyUnits;
            case SectionType.Labs:
                return item.Labs;
            case SectionType.Suggestions:
                return item.Suggestions;
            case SectionType.Bibliography:
                return item.Bibliography;
            default:
                throw new ArgumentOutOfRangeException(nameof(p), p, "Unknown section type");
        }
    }

    public static Enumerator<T> GetEnumerator<T>(this ref readonly OneForEachSectionType<T> item)
    {
        return new(in item);
    }

    public ref struct Enumerator<T>
    {
        private readonly ref readonly OneForEachSectionType<T> _item;
        private int _index;

        public Enumerator(ref readonly OneForEachSectionType<T> item)
        {
            _item = ref item;
            _index = -1;
        }

        public SectionType CurrentType => (SectionType) _index;
        public T Current => _item.Get((SectionType) _index);

        public bool MoveNext()
        {
            _index++;
            return _index < (int) SectionType.Count;
        }

        public void Reset() => _index = -1;
    }
}

internal enum IsHeadingStyleResult
{
    Heading,
    NotHeading,
    NoStyle,
}


internal readonly struct SectionItemsEnumerable
{
    private readonly OpenXmlElementList.Enumerator _e;
    private readonly OpenXmlElement? _nextSectionStart;

    public SectionItemsEnumerable(
        OpenXmlElementList.Enumerator e,
        OpenXmlElement? nextSectionStart)
    {
        _nextSectionStart = nextSectionStart;
        _e = e;
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private OpenXmlElementList.Enumerator _e;
        private readonly OpenXmlElement? _nextSectionStart;

        public Enumerator(SectionItemsEnumerable e)
        {
            _e = e._e;
            _nextSectionStart = e._nextSectionStart;
        }

        public bool MoveNext()
        {
            if (!_e.MoveNext())
            {
                return false;
            }
            if (_e.Current == _nextSectionStart)
            {
                return false;
            }
            return true;
        }

        public OpenXmlElement Current => _e.Current;
    }
}

internal static class PreliminarySectionProcessing
{
    public static Preliminary Process(
        ref ParsingState parsingState,
        SectionItemsEnumerable items)
    {
        PreliminaryState state = new();
        foreach (var it in items)
        {
            if (it is not Paragraph para)
            {
                throw new InvalidOperationException("Only expected paragraphs in the preliminary");
            }
            var parseResult = ParseFieldType(para);
            if (parseResult.IsSection)
            {
                MaybeEndField(parsingState.Accumulator);

                var newType = (PreliminaryField) parseResult.SectionType;
                if (state.Processed.IsSet((int) newType))
                {
                    throw new InvalidOperationException($"'{newType}' field appears twice in the preliminary");
                }

                state.Field = newType;
                state.Processed.Set((int) newType);
            }
            else
            {
                var a = parsingState.Accumulator;
                foreach (var text in para.Descendants<Text>())
                {
                    a.Append(text.Text);
                }
                a.Append('\n');
            }
        }

        MaybeEndField(parsingState.Accumulator);
        return state.Preliminary;

        void MaybeEndField(StringBuilder accumulator)
        {
            if (state.Field == PreliminaryField.None)
            {
                return;
            }

            var str = accumulator.ToStringAndClear();
            if (str.Length == 0)
            {
                throw new NotSupportedException("Subsections in preliminary must not be empty");
            }

            state.Preliminary.Set(state.Field, str);
        }

        static SectionParseResult ParseFieldType(Paragraph para)
        {
            if (!IsHeading(para))
            {
                return SectionParseResult.CreateNotHeading();
            }

            var ret = SectionParser.Parse(para, new PreprocessDoNothing(), PreliminaryFieldStrings);
            SectionParser.DefaultHandleError(ret);
            return ret;
        }

        static bool IsHeading(OpenXmlElement para)
        {
            foreach (var textItem in para.Descendants<Text>())
            {
                if (!HasStyling(textItem))
                {
                    return false;
                }
            }
            return true;

            static bool HasStyling(Text text)
            {
                if (text.Parent is not Run run)
                {
                    return false;
                }
                if (run.GetFirstChild<RunProperties>() is not { } props)
                {
                    return false;
                }
                if (props.Bold is { })
                {
                    return true;
                }
                if (props.Italic is { })
                {
                    return true;
                }
                return false;
            }
        }
    }


    private static readonly ImmutableArray<string> PreliminaryFieldStrings = CreatePreliminaryFieldStrings();
    private static ImmutableArray<string> CreatePreliminaryFieldStrings()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var ret = ImmutableArray.CreateBuilder<string>((int) PreliminaryField.Count);
        ret.Count = (int) PreliminaryField.Count;
        void Set(PreliminaryField field, string value)
        {
            ret[(int) field] = value;
        }
        Set(PreliminaryField.Overview, "Prezentarea generală a cursului");
        Set(PreliminaryField.Importance, "Locul și rolul cursului în formarea rezultatelor învățării ale specialității și misiunea curriculumului în formarea profesională");
        Set(PreliminaryField.Languages, "Limba de predare a cursului");
        Set(PreliminaryField.Beneficiaries, "Beneficiarii");
        Debug.Assert(ret.All(x => x != null));
        return ret.MoveToImmutable();
    }

    private struct PreprocessDoNothing() : IPreprocess
    {
        public void Preprocess(ref Parser parser)
        {
        }
    }
}

internal readonly struct SectionParseResult
{
    public readonly int SectionType;
    public readonly bool IsSection;
    public readonly ReadOnlyMemory<char> UnmatchedText;
    public readonly ReadOnlyMemory<char> MissingText;

    public readonly bool IsUnknown => IsSection && SectionType == -1;

    private SectionParseResult(
        int sectionType,
        bool isSection,
        ReadOnlyMemory<char> unmatchedText = default,
        ReadOnlyMemory<char> missingText = default)
    {
        SectionType = sectionType;
        IsSection = isSection;
        UnmatchedText = unmatchedText;
        MissingText = missingText;
    }

    public static SectionParseResult CreateOk(int sectionType)
    {
        return new(
            sectionType: sectionType,
            isSection: true);
    }
    public static SectionParseResult CreateNotHeading()
    {
        return new(
            sectionType: -1,
            isSection: false);
    }
    public static SectionParseResult CreateUnknown(
        ReadOnlyMemory<char> unmatchedText = default,
        ReadOnlyMemory<char> missingText = default)
    {
        return new(
            sectionType: -1,
            isSection: true,
            unmatchedText: unmatchedText,
            missingText: missingText);
    }
    public static SectionParseResult CreatePartialMatch(
        int sectionType,
        ReadOnlyMemory<char> missingText)
    {
        return new(
            sectionType: sectionType,
            isSection: true,
            missingText: missingText);
    }
}

internal interface IPreprocess
{
    public void Preprocess(ref Parser parser);
}

internal static class SectionParser
{
    public static SectionParseResult Parse<TPreprocess>(
        Paragraph para,
        TPreprocess preprocess,
        ImmutableArray<string> strings)

        where TPreprocess : IPreprocess
    {
        var potentialSectionTypes = BitArray32.AllSet(strings.Length);
        using var readPositions = new RentedBuffer<int>(strings.Length);
        readPositions.Span.Fill(0);

        // It might be split up into multiple text segments, have to check each.
        bool isFirstCheck = true;
        foreach (var textItem in para.Descendants<Text>())
        {
            var parser = new Parser(textItem.Text);
            if (parser.SkipWhitespace().EndOfInput)
            {
                continue;
            }

            preprocess.Preprocess(ref parser);

            var remainingSpan = parser.PeekSpanUntilEnd().Trim();
            if (remainingSpan.Length == 0)
            {
                continue;
            }

            isFirstCheck = false;

            // For now, check for an exact equality.
            // Maybe look for keywords later?
            foreach (var sectionIndex in potentialSectionTypes.SetBitIndicesLowToHigh)
            {
                ref var refStartIndex = ref readPositions.Array[sectionIndex];
                var sectionsString = strings[sectionIndex];
                var currentSlice = sectionsString.AsSpan(refStartIndex);

                if (IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(currentSlice, remainingSpan))
                {
                    // TODO: This is pretty hard to implement correctly.
                    // I need to get the character positions IN THE ORIGINAL string.
                    // This is currently NOT CORRECT.
                    refStartIndex += remainingSpan.Length;
                    var p = new Parser(sectionsString);
                    p.MoveTo(new(refStartIndex));
                    p.SkipWhitespace();
                    refStartIndex = p.Position.Index;
                }
                else
                {
                    potentialSectionTypes.Unset(sectionIndex);
                }
            }

            if (potentialSectionTypes.IsEmpty)
            {
                return SectionParseResult.CreateUnknown(
                    unmatchedText: parser.SourceUntilEnd());
            }
        }

        if (isFirstCheck)
        {
            // Not a single Text descendant.
            return SectionParseResult.CreateNotHeading();
        }

        if (potentialSectionTypes.SetCount > 1)
        {
            return SectionParseResult.CreateUnknown();
        }

        foreach (var sectionIndex in potentialSectionTypes.SetBitIndicesLowToHigh)
        {
            var str = strings[sectionIndex];
            var start = readPositions.Array[sectionIndex];
            Debug.Assert(start != 0, "Can only happen if only checked empty strings");

            if (str.Length == start)
            {
                return SectionParseResult.CreateOk(sectionIndex);
            }
            else
            {
                return SectionParseResult.CreatePartialMatch(sectionIndex, str.AsMemory(start));
            }
        }

        throw UnreachableHelper.Unreachable();
    }

    public static void DefaultHandleError(SectionParseResult x)
    {
        if (!x.IsSection)
        {
            return;
        }
        if (x.IsUnknown)
        {
            throw new NotSupportedException($"Unrecognized heading '{x.UnmatchedText}'.");
        }
        if (!x.MissingText.IsEmpty)
        {
            throw new NotSupportedException($"Partially matched heading '{x.MissingText}'.");
        }
    }
}

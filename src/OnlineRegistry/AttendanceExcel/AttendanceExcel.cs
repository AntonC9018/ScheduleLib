using ClosedXML.Excel;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;

namespace OnlineRegistry.AttendanceExcel;

public enum RepeatedCourseBehavior
{
    Error,
    Warn,
    Ignore,
    Append,
    Replace,
}

public static class AttendanceExcel
{
    public readonly struct ParseAttendanceListsExcelParams
    {
        public required AllStudentAttendanceListBuilder Builder { get; init; }
        public required FilteredSchedule Schedule { get; init; }
        public required XLWorkbook Workbook { get; init; }
        public required GroupParseContext GroupParseContext { get; init; }
        public required LookupModule LookupModule { get; init; }
        public required CourseNameUnifierModule CourseNames { get; init; }
        public required RepeatedCourseBehavior RepeatedCourseBehavior { get; init; }
    }

    private static class NameTokenType
    {
        public const TokenType NamePart = TokenType.Invalid + 1;
    }

    private static readonly TokenTypeLabels _labels =
        LexerHelper.CreateLabelDict(typeof(NameTokenType));

    private sealed class NameTokenReader : ITokenReader
    {
        public static readonly NameTokenReader Instance = new();

        public TokenType Read(ref Parser parser)
        {
            if (parser.SkipWhitespace().SkippedAny)
            {
                return TokenType.Whitespace;
            }
            if (parser.ConsumeExactChar('('))
            {
                return (TokenType) '(';
            }
            if (parser.ConsumeExactChar(')'))
            {
                return (TokenType) ')';
            }
            parser.Skip(new SkipNotWhitespaceOrSep());
            return NameTokenType.NamePart;
        }

        private struct SkipNotWhitespaceOrSep : IShouldSkip
        {
            public bool ShouldSkip(char ch)
            {
                if (ch is '(' or ')')
                {
                    return false;
                }
                if (char.IsWhiteSpace(ch))
                {
                    return false;
                }
                return true;
            }
        }
    }

    private struct Key()
    {
        public CourseId CourseId = CourseId.Invalid;
        public LessonGroups Groups = [];
        public SubGroup SubGroup = SubGroup.All;
        public LessonType LessonType = LessonType.Lab;
    }

    private struct ParsedName
    {
        public required (ParserPosition Start, ParserPosition End)? NameRange;
        public required LessonType LessonType;
        public required SubGroup SubGroup;
        public required Group? Group;
    }

    private static ParsedName ParseName(
        Lexer lexer,
        GroupParseContext groupParser)
    {
        ParserPosition? nameStart = null;
        ParserPosition? nameEnd = null;
        bool isInParens = false;
        var lessonType = LessonType.Lab;
        var subGroup = SubGroup.All;
        Group? group = null;

        while (!lexer.IsEmpty)
        {
            var token = lexer.Peek();
            switch (token.Type)
            {
                case NameTokenType.NamePart:
                {
                    if (isInParens)
                    {
                        if (LessonTypeParser.Instance.Parse(token.Value.Span) is { } lessonType1)
                        {
                            lessonType = lessonType1;
                            break;
                        }
                        throw new InvalidOperationException($"Lesson type {token.Value.Span} is not a valid lesson type");
                    }
                    if (NumberHelper.FromRoman(token.Value.Span) is { } ord)
                    {
                        _ = ord;
                        subGroup = new(token.Value.ToString());
                        break;
                    }
                    if (groupParser.TryParse(token.Value) is { } x)
                    {
                        group = x;
                        break;
                    }

                    if (nameStart == null)
                    {
                        nameStart = token.Span.ColStart;
                    }
                    nameEnd = token.Span.ColEnd;
                    break;
                }
                case (TokenType) '(':
                {
                    isInParens = true;
                    break;
                }
                case (TokenType) ')':
                {
                    isInParens = false;
                    break;
                }
            }
            lexer.Move();
        }

        return new()
        {
            Group = group,
            LessonType = lessonType,
            NameRange = nameStart is null ? null : (nameStart.Value, nameEnd!.Value),
            SubGroup = subGroup,
        };
    }

#pragma warning disable CA1001 // undisposed field
    private struct ParseNameHelper
#pragma warning restore CA1001
    {
        private readonly SingleItemEnumerator<string> _nameE;
        private readonly Lexer _lexer;

        private readonly FilteredSchedule _schedule;
        private readonly CourseNameUnifierModule _courseNames;
        private readonly GroupParseContext _groupParseContext;
        private readonly LookupModule _lookupModule;

        public ParseNameHelper(
            FilteredSchedule schedule,
            CourseNameUnifierModule courseNames,
            GroupParseContext groupParseContext,
            LookupModule lookupModule)
        {
            _nameE = new SingleItemEnumerator<string>();
            _lexer = new Lexer(NameTokenReader.Instance, _labels);
            _schedule = schedule;
            _courseNames = courseNames;
            _groupParseContext = groupParseContext;
            _lookupModule = lookupModule;
        }

        public AnyLessonAccessor? LookupLessonByExcelName(string excelName)
        {
            _nameE.Reset(excelName);
            _lexer.Reset(_nameE);

            var parsedName = ParseName(_lexer, _groupParseContext);
            var courseId = CourseId.Invalid;
            if (parsedName.NameRange is { } nameRange)
            {
                var courseName = excelName.AsMemory()[nameRange.Start.Index .. nameRange.End.Index];
                if (_courseNames.Find(new()
                    {
                        CourseName = courseName,
                        Lookup = _lookupModule,
                    }) is not { } x)
                {
                    throw new InvalidOperationException($"Course {courseName} not found");
                }
                courseId = x;
            }

            var groups = new LessonGroups();
            if (parsedName.Group is { } group)
            {
                if (!_lookupModule.Groups.TryGetValue(group.Name, out var x))
                {
                    throw new InvalidOperationException($"Group {group.Name} not found");
                }
                groups = [x];
            }

            var key = new Key
            {
                CourseId = courseId,
                Groups = groups,
                LessonType = parsedName.LessonType,
                SubGroup = parsedName.SubGroup,
            };
            return LookupLesson(key, _schedule);
        }

        private static AnyLessonAccessor? LookupLesson(
            Key key,
            FilteredSchedule schedule)
        {
            var diffLesson = default(LessonData);
            var diffMask = new LessonModelDiffMask();
            {
                if (!key.CourseId.IsInvalid)
                {
                    diffLesson.Course = key.CourseId;
                    diffMask.Course = true;
                }
            }
            {
                if (key.Groups.Count > 0)
                {
                    diffLesson.Groups = key.Groups;
                    diffMask.AllGroups = true;
                }
            }
            {
                diffLesson.SubGroup = key.SubGroup;
                diffMask.SubGroup = true;
            }
            {
                diffLesson.Type = key.LessonType;
                diffMask.LessonType = true;
            }

            LessonData result = default;
            var resultDiffMask = new LessonModelDiffMask
            {
                LessonType = true,
                SubGroup = true,
                AllGroups = true,
                Course = true,
                AllTeachers = true,
            };
            AnyLessonId? lessonId = null;

            foreach (var lesson in schedule.EnumerateLessons())
            {
                if (result != default)
                {
                    var differences = LessonBuilderHelper.Diff(lesson.Lesson, result, resultDiffMask);
                    if (differences.Intersect(diffMask).TheyDiffer)
                    {
                        continue;
                    }
                    if (differences.TheyDiffer)
                    {
                        throw new InvalidOperationException("Multiple matches to the partial key");
                    }
                    continue;
                }
                {

                    var differences = LessonBuilderHelper.Diff(lesson.Lesson, diffLesson, diffMask);
                    if (!differences.TheyAreEqual)
                    {
                        continue;
                    }
                    result = lesson.Lesson;
                    lessonId = lesson.Id;
                }
            }

            if (lessonId is { } val)
            {
                return schedule.Source.Get(val);
            }
            return null;
        }
    }

    public static void ParseAttendanceListsExcel(ParseAttendanceListsExcelParams p)
    {
        var helper = new ParseNameHelper(
            schedule: p.Schedule,
            courseNames: p.CourseNames,
            groupParseContext: p.GroupParseContext,
            lookupModule: p.LookupModule);

        foreach (var sheet in p.Workbook.Worksheets)
        {
            if (helper.LookupLessonByExcelName(sheet.Name) is not { } lesson)
            {
                throw new InvalidOperationException($"Not found lesson for string {sheet.Name}");
            }

            ref readonly var g = ref lesson.Lesson.Groups;
            if (!g.IsSingleGroup)
            {
                Add(g);
            }
            foreach (var group in g)
            {
                Add([group]);
            }

            StudentsLookupKey Key(in LessonGroups groups)
            {
                return new(
                    courseId: lesson.Lesson.Course,
                    groups: groups,
                    subGroup: lesson.Lesson.SubGroup,
                    lessonType: lesson.Lesson.Type);
            }

            void Add(in LessonGroups groups)
            {
                var key = Key(groups);
                var l = p.Builder.TryList(key);
                if (!l.Existed)
                {
                    BuildList(sheet, l.Builder);
                    return;
                }

                switch (p.RepeatedCourseBehavior)
                {
                    case RepeatedCourseBehavior.Append:
                    {
                        BuildList(sheet, l.Builder);
                        break;
                    }
                    case RepeatedCourseBehavior.Ignore:
                    {
                        break;
                    }
                    case RepeatedCourseBehavior.Warn:
                    {
                        Console.WriteLine($"Repeated course: {p.Schedule.Source.Get(lesson.Lesson.Course).FullName}");
                        break;
                    }
                    case RepeatedCourseBehavior.Replace:
                    {
                        l.Builder.Clear();
                        BuildList(sheet, l.Builder);
                        break;
                    }
                    case RepeatedCourseBehavior.Error:
                    {
                        throw new RepeatedCourseException();
                    }
                }
            }
        }
    }

    // TODO: reuse the list
    private static void BuildList(IXLWorksheet sheet, StudentAttendanceListBuilder list)
    {
        // ReSharper disable once GenericEnumeratorNotDisposed
        using var rowE = sheet.Rows().GetEnumerator().RememberIsDone();

        while (true)
        {
            if (!rowE.MoveNext())
            {
                break;
            }
            var row = rowE.Current;

            using var cells = row.Cells(usedCellsOnly: false).GetEnumerator();
            if (!cells.MoveNext())
            {
                break;
            }

            if (!cells.Current!.TryGetValue(out string value))
            {
                break;
            }
            var parser = new Parser(value);
            if (NameHelper.TryParseName(ref parser) is not { } name)
            {
                break;
            }

            var student = list.Student(name);

            while (cells.MoveNext())
            {
                if (!cells.Current!.TryGetValue(out string attendanceStr))
                {
                    throw new InvalidOperationException("Expecting a string in cell");
                }
                var attendance = AttendanceHelper.Parse(attendanceStr);
                if (attendance == Attendance.Grade
                    || attendance == Attendance.None)
                {
                    throw new InvalidOperationException($"Expecting empty, 'a', 'na' or 'am', got '{attendanceStr}'");
                }

                student.Day(attendance);
            }

        }

        int maxLen = 0;
        while (!rowE.IsDone)
        {
            // find the last cell that has any value.
            var c = rowE.Current;
            var lastNonEmpty = c.Cells()
                .WithIndex()
                .LastOrDefault(x => x.Item.TryGetValue<string>(out var s) && s is not null and not "");
            maxLen = Math.Max(maxLen, lastNonEmpty.Index);
            rowE.MoveNext();
        }

        list.HintMaxCount(maxLen);
    }
}

public sealed class RepeatedCourseException : Exception
{
}

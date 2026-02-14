using System.Diagnostics;
using ClosedXML.Excel;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Excel.Helper;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;
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

public readonly struct ParseAttendanceListsExcelParams
{
    public AllStudentAttendanceListBuilder Builder { get; }
    public FilteredSchedule Schedule { get; }
    public XLWorkbook Workbook { get; }
    public GroupParseContext GroupParseContext { get; }
    public LookupFacade Lookup { get; }
    public LessonTypeParser LessonTypeParser { get; }

    // Like this is nonsense honestly. Why do I have to care about this so much?
    public readonly /* ref readonly */ AttendanceExcel.WorksheetParseParameters ParseParameters;

    public ParseAttendanceListsExcelParams(
        in AttendanceExcel.WorksheetParseParameters parseParameters,
        AllStudentAttendanceListBuilder builder,
        FilteredSchedule schedule,
        XLWorkbook workbook,
        GroupParseContext groupParseContext,
        LookupFacade lookup,
        LessonTypeParser lessonTypeParser)
    {
        ParseParameters = parseParameters;
        Builder = builder;
        Schedule = schedule;
        Workbook = workbook;
        GroupParseContext = groupParseContext;
        Lookup = lookup;
        LessonTypeParser = lessonTypeParser;
    }
}

public static class AttendanceExcel
{
    private static class NameTokenType
    {
        public const TokenType NamePart = TokenType.Invalid + 1;
    }

    private static readonly TokenTypeLabels _labels =
        LexerHelper.CreateLabels(typeof(NameTokenType));

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
        private readonly SingleItemEnumerator<ReadOnlyMemory<char>> _nameE;
        private readonly Lexer _lexer;

        private readonly FilteredSchedule _schedule;
        private readonly GroupParseContext _groupParseContext;
        private readonly LookupFacade _lookup;

        public ParseNameHelper(
            FilteredSchedule schedule,
            GroupParseContext groupParseContext,
            LookupFacade lookup,
            Lexer lexer)
        {
            _nameE = new();
            _schedule = schedule;
            _groupParseContext = groupParseContext;
            _lookup = lookup;
            _lexer = lexer;
        }

        public AnyLessonAccessor? LookupLessonByExcelName(
            string excelName,
            LessonModelDiffMask additionallyIgnoredFields)
        {
            _nameE.Reset(excelName.AsMemory());
            _lexer.Reset(_nameE);

            var parsedName = ParseName(_lexer, _groupParseContext);
            var courseId = CourseId.Invalid;
            if (parsedName.NameRange is { } nameRange)
            {
                var courseName = excelName.AsMemory()[nameRange.Start.Index .. nameRange.End.Index];
                if (_lookup.Course(courseName) is not { } x)
                {
                    throw new InvalidOperationException($"Course {courseName} not found");
                }
                courseId = x;
            }

            var groups = new LessonGroups();
            if (parsedName.Group is { } group)
            {
                if (_lookup.Group(group.Name) is not { } x)
                {
                    throw new InvalidOperationException($"Group {group.Name} not found");
                }
                groups.Add(x);
            }

            var key = new Key
            {
                CourseId = courseId,
                Groups = groups,
                LessonType = parsedName.LessonType,
                SubGroup = parsedName.SubGroup,
            };
            return LookupLesson(key, _schedule, additionallyIgnoredFields);
        }

        private static AnyLessonAccessor? LookupLesson(
            Key key,
            FilteredSchedule schedule,
            LessonModelDiffMask additionallyIgnoredFields)
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
            diffMask = diffMask.Remove(additionallyIgnoredFields);

            LessonData result = default;
            AnyLessonId? lessonId = null;

            var resultDiffMask = new LessonModelDiffMask
            {
                LessonType = true,
                SubGroup = true,
                AllGroups = true,
                Course = true,
                AllTeachers = true,
            }.Remove(additionallyIgnoredFields);

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
        var lexer = new Lexer(NameTokenReader.Instance, _labels);
        var helper = new ParseNameHelper(
            schedule: p.Schedule,
            lookup: p.Lookup,
            groupParseContext: p.GroupParseContext,
            lexer: lexer);

        foreach (var sheet in p.Workbook.Worksheets)
        {
            var additionallyIgnoredFields = new LessonModelDiffMask
            {
                LessonType = p.ParseParameters.HeaderFormat.FormatType == HeaderFormatType.LessonType,
            };
            if (helper.LookupLessonByExcelName(sheet.Name, additionallyIgnoredFields) is not { } lesson)
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

                void B()
                {
                    BuildList(
                        sheet: sheet,
                        list: l.Builder,
                        headerDeps: new()
                        {
                            LessonTypeParser = p.LessonTypeParser,
                        },
                        p: p.ParseParameters);
                }

                if (!l.Existed)
                {
                    B();
                    return;
                }

                switch (p.ParseParameters.RepeatedCourseBehavior)
                {
                    case RepeatedCourseBehavior.Append:
                    {
                        B();
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
                        B();
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

    public enum HeaderFormatType
    {
        Nothing,
        LessonType,
        IgnoreHeader,
    }

    public struct HeaderFormat()
    {
        public HeaderFormatType FormatType = HeaderFormatType.Nothing;
        public bool IgnoreValuesOutsideHeader = true;
    }

    public enum CellValueFormat
    {
        NotAttendanceIsError,
        IgnoreGrade,
    }

    public struct WorksheetParseParameters()
    {
        public HeaderFormat HeaderFormat = new();
        public CellValueFormat CellValueFormat = CellValueFormat.NotAttendanceIsError;
        public RepeatedCourseBehavior RepeatedCourseBehavior = RepeatedCourseBehavior.Error;
    }

    private readonly struct ParsedHeaderInfo : IDisposable
    {
        private readonly RentedBuffer<LessonType> _lessons;
        public readonly ColumnOffset Offset { get; }
        public bool IsApplicable => _lessons.IsValid;
        public int Length => _lessons.Length;

        public ReadOnlySpan<LessonType> LessonTypes => _lessons.Span;

        public ParsedHeaderInfo(
            RentedBuffer<LessonType> lessons,
            ColumnOffset offset)
        {
            _lessons = lessons;
            Offset = offset;
        }

        public LessonType GetLesson(RestoredIndex index)
        {
            return _lessons.Span[index.Value];
        }

        public void Dispose()
        {
            if (_lessons.IsValid)
            {
                _lessons.Dispose();
            }
        }
    }

    private readonly struct HeaderDeps
    {
        public required LessonTypeParser LessonTypeParser { get; init; }
    }

    // TODO: reuse the list
    private static void BuildList(
        IXLWorksheet sheet,
        StudentAttendanceListBuilder list,
        HeaderDeps headerDeps,
        in WorksheetParseParameters p)
    {
        // ReSharper disable once GenericEnumeratorNotDisposed
        using var rowE = sheet.Rows().GetEnumerator().RememberIsDone();
        using var header = ParseHeader(p.HeaderFormat);
        if (header.IsApplicable)
        {
            list.AddLessonTypes(header.LessonTypes);
        }

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

            var cell = cells.Current!;
            if (!cell.TryGetValue(out string value))
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

                if (attendanceStr != ""
                    && header.IsApplicable)
                {
                    var columnNumber = cell.AsRange().FirstColumn().ColumnNumber();
                    var restoredIndex = header.Offset.GetUnOffsetIndex(columnNumber);
                    if (restoredIndex.Value < 0 || restoredIndex.Value >= header.Length)
                    {
                        if (p.HeaderFormat.IgnoreValuesOutsideHeader)
                        {
                            continue;
                        }
                        throw cell.Exception("The given cell is not within the range of the table ");
                    }
                }

                var attendance = AttendanceHelper.Parse(attendanceStr);
                if (attendance == Attendance.Grade
                    || attendance == Attendance.None)
                {
                    if (p.CellValueFormat == CellValueFormat.NotAttendanceIsError)
                    {
                        throw new InvalidOperationException($"Expecting empty, 'a', 'na' or 'am', got '{attendanceStr}'");
                    }
                    else
                    {
                        Debug.Assert(p.CellValueFormat == CellValueFormat.IgnoreGrade);
                        attendance = Attendance.Present;
                    }
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
        return;

        ParsedHeaderInfo ParseHeader(in HeaderFormat headerFormat)
        {
            if (headerFormat.FormatType == HeaderFormatType.Nothing)
            {
                return default;
            }
            if (!rowE.MoveNext())
            {
                throw sheet.Exception("Expected a header row");
            }
            if (headerFormat.FormatType == HeaderFormatType.IgnoreHeader)
            {
                return default;
            }

            var context = new HeaderParsingContext();
            if (headerFormat.FormatType == HeaderFormatType.LessonType)
            {
                var row = rowE.Current;
                var cellCount = row.CellCount();
                var ret = new RentedBuffer<LessonType>(cellCount);
                try
                {
                    using var cellE = row.Cells(usedCellsOnly: false).GetEnumerator();
                    var retBuilder = ret.Builder();
                    while (true)
                    {
                        if (!cellE.MoveNext())
                        {
                            break;
                        }
                        var cell = cellE.Current!;
                        if (ValidateAndMaybeSkipEmpty(cell, ref context))
                        {
                            continue;
                        }

                        if (!cell.Value.TryGetText(out string strValue))
                        {
                            throw cell.Exception("Expected the cell to have a value");
                        }
                        if (headerDeps.LessonTypeParser.Parse(strValue) is not { } lessonType)
                        {
                            var examples = string.Join(",", headerDeps.LessonTypeParser.AllowedValuesExamples);
                            throw cell.Exception($"{strValue} is an invalid lesson type. The valid values are: {examples}");
                        }
                        retBuilder.Add(lessonType);
                        continue;

                        static bool ValidateAndMaybeSkipEmpty(
                            IXLCell cell,
                            ref HeaderParsingContext c)
                        {
                            var range = cell.AsRange();
                            if (range.IsMerged())
                            {
                                throw cell.Exception("Merged cells are just not supported, don't use them");
                            }

                            var value = cell.Value;
                            // Handle empty cells
                            if (c.FirstOffset is null)
                            {
                                if (value.IsBlank)
                                {
                                    throw cell.Exception("First column of the header row must be empty");
                                }
                                var colNumber = range.FirstColumn().ColumnNumber();
                                c.FirstOffset = new(colNumber);
                                return true;
                            }
                            else if (c.IsInEmptyStreak)
                            {
                                if (!value.IsBlank)
                                {
                                    throw cell.Exception("Cannot have a non-empty value after an empty streak");
                                }
                                return true;
                            }
                            else
                            {
                                if (value.IsBlank)
                                {
                                    c.IsInEmptyStreak = true;
                                    return true;
                                }
                            }

                            {
                                // Some sanity checks.
                                Debug.Assert(!c.IsInEmptyStreak);
                                Debug.Assert(c.FirstOffset.HasValue);
                                var expectedOffset = c.FirstOffset.Value.Value + 1;
                                var colNumber = range.FirstColumn().ColumnNumber();
                                if (expectedOffset != colNumber)
                                {
                                    throw cell.Exception("Unexpected column number");
                                }
                            }
                            return false;
                        }
                    }
                }
                catch
                {
                    ret.Dispose();
                    throw;
                }
                return new(ret, context.FirstOffset ?? default);
            }
            throw Unreachable();
        }

    }

    private struct HeaderParsingContext()
    {
        public ColumnOffset? FirstOffset = null;
        public bool IsInEmptyStreak = false;
    }
}

public sealed class RepeatedCourseException : Exception
{
}

using System.Diagnostics;
using AutoConstructor.Attributes;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Excel.Helper;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using Group = ScheduleLib.Group;
using SequencePosition = ScheduleLib.Helper.Parsing.SequencePosition;

namespace OnlineRegistry.AttendanceExcel;

public enum RepeatedCourseBehavior
{
    Error,
    Warn,
    Ignore,
    Append,
    Replace,
}

[AutoConstructor]
public sealed partial class AttendanceListsExcelParser
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<AttendanceListsExcelParser>();
    }

    private readonly GroupParseContext _groupParser;
    private readonly LookupFacade _lookup;
    private readonly LessonTypeParser _lessonTypeParser;
    private readonly SubGroupNameRemapper _subGroupRemapper;
    private readonly SpecializationRegistry _specializationRegistry;

    public void Parse(ParseAttendanceListsExcelParams p)
    {
        var lexer = new Lexer(NameTokenReader.Instance);
        var helper = new ParseNameHelper(
            schedule: p.Schedule,
            lookup: _lookup,
            groupParseContext: _groupParser,
            subGroupRemapper: _subGroupRemapper,
            specializationRegistry: _specializationRegistry,
            lexer: lexer);

        foreach (var sheet in p.Workbook.Worksheets)
        {
            using var header = ParseHeaderInfo(
                sheet,
                headerDeps: new()
                {
                    LessonTypeParser = _lessonTypeParser,
                },
                p.ParseParameters.HeaderFormat);
            var additionallyIgnoredFields = new LessonModelDiffMask();
            switch (header.HeaderType)
            {
                case HeaderType.LessonType:
                {
                    additionallyIgnoredFields.LessonType = true;
                    break;
                }
                case HeaderType.None:
                {
                    break;
                }
                default:
                {
                    throw Unreachable();
                }
            }
            if (helper.LookupLessonByExcelName(sheet.Name, additionallyIgnoredFields) is not { } lesson)
            {
                throw new InvalidOperationException($"Not found lesson for string {sheet.Name}");
            }

            ref readonly var g = ref lesson.Groups;
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
                    courseId: lesson.Course,
                    groups: groups,
                    groupPartition: lesson.GroupPartition,
                    lessonType: lesson.Type);
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
                        header,
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
                        Console.WriteLine($"Repeated course: {p.Schedule.Source.Get(lesson.Course).FullName}");
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

    private static class NameTokenType
    {
        public const TokenType NamePart = TokenType.Invalid + 1;
    }

    private sealed class NameTokenReader : ITokenReader
    {
        public TokenTypeLabels Labels { get; } =
            LexerHelper.CreateLabels(typeof(NameTokenType));

        public static readonly NameTokenReader Instance = new();

        public TokenType Read(ref SequenceReader reader)
        {
            if (reader.SkipWhitespace().SkippedAny)
            {
                return TokenType.Whitespace;
            }
            if (reader.ConsumeExactChar('('))
            {
                return (TokenType) '(';
            }
            if (reader.ConsumeExactChar(')'))
            {
                return (TokenType) ')';
            }
            reader.Skip(new SkipNotWhitespaceOrSep());
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
        public GroupPartitionKey GroupPartition = GroupPartitionKey.All;
        public LessonType LessonType = LessonType.Lab;
    }

    private struct ParsedName
    {
        public required (SequencePosition Start, SequencePosition End)? NameRange;
        public required LessonType LessonType;
        public required GroupPartitionKey GroupPartition;
        public required Group? Group;
    }

    private static ParsedName ParseName(
        Lexer lexer,
        GroupParseContext groupParser,
        SubGroupNameRemapper subGroupRemapper,
        SpecializationRegistry specializationRegistry)
    {
        SequencePosition? nameStart = null;
        SequencePosition? nameEnd = null;
        bool isInParens = false;
        var lessonType = LessonType.None;
        var groupPartition = GroupPartitionKey.All;
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
                        else if (GroupPartition())
                        {
                            break;
                        }
                        throw new InvalidOperationException($"Lesson type {token.Value.Span} is not a valid lesson type");
                    }
                    if (GroupPartition())
                    {
                        break;
                    }
                    if (Group())
                    {
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

            bool GroupPartition()
            {
                var remapped = subGroupRemapper.Remap(token.Value);
                if (NumberHelper.FromRoman(remapped.Value) is { } ord)
                {
                    _ = ord;
                    groupPartition = new(remapped, Specialization.All);
                    return true;
                }
                if (Specializations.TryFromValue(remapped.Value, out var specialization)
                    || specializationRegistry.TryFromValue(remapped.Value, out specialization))
                {
                    groupPartition = new(SubGroup.All, specialization);
                    return true;
                }
                return false;
            }
            bool Group()
            {
                if (groupParser.TryParse(token.Value) is { } x)
                {
                    group = x;
                    return true;
                }
                return false;
            }
        }

        return new()
        {
            Group = group,
            LessonType = lessonType,
            NameRange = nameStart is null ? null : (nameStart.Value, nameEnd!.Value),
            GroupPartition = groupPartition,
        };
    }

    private readonly record struct LookedUpLesson(
        LessonType Type,
        LessonGroups Groups,
        GroupPartitionKey GroupPartition,
        CourseId Course)
    {
        public readonly LessonGroups Groups = Groups;
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
        private readonly SubGroupNameRemapper _subGroupRemapper;
        private readonly SpecializationRegistry _specializationRegistry;

        public ParseNameHelper(
            FilteredSchedule schedule,
            GroupParseContext groupParseContext,
            LookupFacade lookup,
            SubGroupNameRemapper subGroupRemapper,
            SpecializationRegistry specializationRegistry,
            Lexer lexer)
        {
            _nameE = new();
            _schedule = schedule;
            _groupParseContext = groupParseContext;
            _lookup = lookup;
            _subGroupRemapper = subGroupRemapper;
            _specializationRegistry = specializationRegistry;
            _lexer = lexer;
        }

        public LookedUpLesson? LookupLessonByExcelName(
            string excelName,
            LessonModelDiffMask additionallyIgnoredFields)
        {
            _nameE.Reset(excelName.AsMemory());
            _lexer.Reset(_nameE);

            var parsedName = ParseName(
                _lexer,
                _groupParseContext,
                _subGroupRemapper,
                _specializationRegistry);
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
                GroupPartition = parsedName.GroupPartition,
            };
            var ret = LookupLesson(key, _schedule, additionallyIgnoredFields);
            if (ret == null)
            {
                var schedule = _schedule;
                _ = schedule;
                var lessons = _schedule.EnumerateLessons().ToArray();
                _ = lessons;
                Debugger.Break();
            }
            if (ret is not { } r)
            {
                return null;
            }
            var l = r.Lesson;
            if (additionallyIgnoredFields.LessonType)
            {
                l.Type = LessonType.None;
            }
            return new(
                l.Type,
                l.Groups,
                l.GroupPartitionKey,
                l.Course);
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
                diffLesson.SubGroup = key.GroupPartition.SubGroup;
                diffLesson.Specialization = key.GroupPartition.Specialization;
                diffMask.SubGroup = true;
            }
            if (key.LessonType != LessonType.None)
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
                        throw new InvalidOperationException($"Multiple matches to the partial key {differences}");
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
                    var x = LessonBuilderHelper.Diff(lesson.Lesson, diffLesson, diffMask);
                    _ = x;
                }
            }

            if (lessonId is { } val)
            {
                return schedule.Source.Get(val);
            }
            return null;
        }
    }

    private readonly struct ParsedHeaderInfo : IDisposable
    {
        private readonly RentedBuffer<LessonType> _lessons;
        public readonly ColumnOffset Offset { get; }
        public bool IsApplicable => HeaderType != HeaderType.None;
        public int Len => _lessons.Len;
        public readonly HeaderType HeaderType = HeaderType.None;

        public ReadOnlySpan<LessonType> LessonTypes => _lessons.Span;

        public ParsedHeaderInfo(
            RentedBuffer<LessonType> lessons,
            ColumnOffset offset,
            HeaderType headerType)
        {
            _lessons = lessons;
            Offset = offset;
            HeaderType = headerType;
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

    private enum HeaderType
    {
        None,
        LessonType,
        // ...
    }

    private struct ParsedHeaderInfoBuilder() : IDisposable
    {
        private const int UnspecifiedLen = -1;
        private HeaderType _headerType = HeaderType.None;
        private int _len = UnspecifiedLen;
        private RentedBuffer<LessonType> _lessons;
        private int _count = 0;
        #if DEBUG
        private bool _built = false;
        #endif

        public void SetLen(int len)
        {
            Debug.Assert(_len == UnspecifiedLen && len >= 0);
            _len = len;
        }

        public void DeclareIsLesson()
        {
            Debug.Assert(_headerType is HeaderType.None or HeaderType.LessonType);
            Debug.Assert(_len != UnspecifiedLen);
            if (_headerType == HeaderType.None)
            {
                _headerType = HeaderType.LessonType;
                _lessons = new RentedBuffer<LessonType>(_len);
            }
        }

        public bool IsEmpty => _count == 0;

        public void AddLesson(LessonType t)
        {
            DeclareIsLesson();
            int i = _count;
            Debug.Assert(i <= _len);
            _lessons.Span[i] = t;
            _count++;
        }

        public void Dispose()
        {
            if (_lessons.IsValid)
            {
                _lessons.Dispose();
            }
        }

        public ParsedHeaderInfo Build(ColumnOffset columnOffset)
        {
            #if DEBUG
            Debug.Assert(!_built);
            _built = true;
            #endif
            var ret = new ParsedHeaderInfo(
                _lessons.WithLen(_count),
                columnOffset,
                _headerType);
            // Move into info so it can be disposed.
            _lessons = default;
            return ret;
        }
    }

    private readonly struct HeaderDeps
    {
        public required LessonTypeParser LessonTypeParser { get; init; }
    }

    private static ParsedHeaderInfo ParseHeaderInfo(
        IXLWorksheet sheet,
        HeaderDeps headerDeps,
        HeaderFormat headerFormat)
    {
        IXLRow? row = sheet.Rows().FirstOrDefault();
        if (headerFormat.FormatType == HeaderFormatType.Nothing)
        {
            return default;
        }
        if (row == null)
        {
            if (headerFormat.FormatType == HeaderFormatType.Auto)
            {
                return default;
            }
            throw sheet.Exception("Expected a header row");
        }

        if (headerFormat.FormatType == HeaderFormatType.IgnoreHeader)
        {
            return default;
        }

        Debug.Assert(headerFormat.FormatType is HeaderFormatType.LessonType or HeaderFormatType.Auto,
            $"Not implemented: {headerFormat.FormatType}");

        var context = new HeaderParsingContext(headerFormat.FormatType);
        using var builder = new ParsedHeaderInfoBuilder();
        {
            var cellCount = row.CellCount();
            builder.SetLen(cellCount);
        }

        using var cellE = row.Cells(usedCellsOnly: false).GetEnumerator();
        while (true)
        {
            if (!cellE.MoveNext())
            {
                break;
            }
            var cell = cellE.Current!;

            var action = HeaderValidateAndMaybeSkipEmptyOrValidateFormat(cell, ref context);
            if (action == HeaderColumnValidationResult.SkipEmpty)
            {
                continue;
            }
            if (action == HeaderColumnValidationResult.NotAHeader)
            {
                return default;
            }

            if (!cell.Value.TryGetText(out string strValue))
            {
                throw cell.Exception("Expected the cell to have a value");
            }

            switch (context.Format)
            {
                case HeaderFormatType.Auto:
                {
                    if (TryLessonType())
                    {
                        // Select the type
                        context.Format = HeaderFormatType.LessonType;
                        continue;
                    }
                    if (builder.IsEmpty)
                    {
                        return default;
                    }
                    throw Unreachable();
                }
                case HeaderFormatType.LessonType:
                {
                    if (!TryLessonType())
                    {
                        var examples = string.Join(",", headerDeps.LessonTypeParser.AllowedValuesExamples);
                        throw cell.Exception($"{strValue} is an invalid lesson type. The valid values are: {examples}");
                    }
                    continue;
                }
                default:
                {
                    throw Unreachable();
                }
            }

            bool TryLessonType()
            {
                if (headerDeps.LessonTypeParser.Parse(strValue) is not { } lessonType)
                {
                    return false;
                }
                builder.AddLesson(lessonType);
                return true;
            }
        }
        return builder.Build(context.FirstOffset ?? default);
    }

    // TODO: reuse the list
    private static void BuildList(
        IXLWorksheet sheet,
        StudentAttendanceListBuilder list,
        in ParsedHeaderInfo header,
        in WorksheetParseParameters p)
    {
        // ReSharper disable once GenericEnumeratorNotDisposed
        using var rowE = sheet.Rows().GetEnumerator().RememberIsDone();
        if (header.HeaderType != HeaderType.None)
        {
            var x = rowE.MoveNext();
            Debug.Assert(x);
        }

        switch (header.HeaderType)
        {
            case HeaderType.LessonType:
            {
                list.AddLessonTypes(header.LessonTypes);
                break;
            }
            case HeaderType.None:
            {
                break;
            }
            default:
            {
                throw Unreachable();
            }
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

            var parser = new SequenceReader(value);
            if (NameHelper.TryParseName(ref parser) is not { } name)
            {
                break;
            }

            var student = list.Student(name);
            int consecutiveDefaultCount = 0;

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
                    if (restoredIndex.Value < 0 || restoredIndex.Value >= header.Len)
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
                if (attendance == Attendance.Present)
                {
                    consecutiveDefaultCount++;
                }
                else
                {
                    for (int i = 0; i < consecutiveDefaultCount; i++)
                    {
                        student.Day(Attendance.Present);
                    }
                    consecutiveDefaultCount = 0;
                    student.Day(attendance);
                }
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
    }

    private enum HeaderColumnValidationResult
    {
        SkipEmpty,
        NotAHeader,
        Value,
    }

    private static HeaderColumnValidationResult HeaderValidateAndMaybeSkipEmptyOrValidateFormat(
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
            var columnNumber = cell.AsRange().FirstColumn().ColumnNumber();
            if (columnNumber == 1)
            {
                if (!value.IsBlank)
                {
                    if (c.Format == HeaderFormatType.Auto)
                    {
                        return HeaderColumnValidationResult.NotAHeader;
                    }
                    throw cell.Exception("First column of the header row must be empty");
                }
                c.FirstOffset = new(columnNumber);
                c.PreviousOffset = new(columnNumber);
                return HeaderColumnValidationResult.SkipEmpty;
            }
            else if (columnNumber == 2)
            {
                c.FirstOffset = new(columnNumber - 1);
                c.PreviousOffset = new(columnNumber - 1);
            }
        }

        if (c.IsInEmptyStreak)
        {
            if (!value.IsBlank)
            {
                throw cell.Exception("Cannot have a non-empty value after an empty streak");
            }
            return HeaderColumnValidationResult.SkipEmpty;
        }
        else
        {
            if (value.IsBlank)
            {
                c.IsInEmptyStreak = true;
                return HeaderColumnValidationResult.SkipEmpty;
            }
        }

        {
            // Some sanity checks.
            Debug.Assert(!c.IsInEmptyStreak);
            Debug.Assert(c.FirstOffset.HasValue);
            var expectedOffset = c.PreviousOffset.Value + 1;
            var colNumber = range.FirstColumn().ColumnNumber();
            if (expectedOffset != colNumber)
            {
                throw cell.Exception("Unexpected column number");
            }
            c.PreviousOffset = new(colNumber);
        }
        return HeaderColumnValidationResult.Value;
    }

    private struct HeaderParsingContext(HeaderFormatType format)
    {
        public HeaderFormatType Format = format;
        public ColumnOffset? FirstOffset = null;
        public ColumnOffset PreviousOffset = default;
        public bool IsInEmptyStreak = false;
        public bool AllowedToSkipEmpty = format != HeaderFormatType.Auto;
    }
}


public enum HeaderFormatType
{
    Nothing,
    LessonType,
    IgnoreHeader,
    Auto,
}

public struct HeaderFormat()
{
    public HeaderFormatType FormatType = HeaderFormatType.Auto;
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


public readonly struct ParseAttendanceListsExcelParams
{
    public AllStudentAttendanceListBuilder Builder { get; }
    public FilteredSchedule Schedule { get; }
    public XLWorkbook Workbook { get; }

    // Like this is nonsense honestly. Why do I have to care about this so much?
    public readonly /* ref readonly */ WorksheetParseParameters ParseParameters;

    public ParseAttendanceListsExcelParams(
        in WorksheetParseParameters parseParameters,
        AllStudentAttendanceListBuilder builder,
        FilteredSchedule schedule,
        XLWorkbook workbook)
    {
        ParseParameters = parseParameters;
        Builder = builder;
        Schedule = schedule;
        Workbook = workbook;
    }
}

public sealed class RepeatedCourseException : Exception
{
}

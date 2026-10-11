using System.Diagnostics;
using System.Globalization;
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
        if (p.ParseParameters.StudentNameColumn is < 1)
            throw new ArgumentOutOfRangeException(nameof(p), "Student name column must be positive.");
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
            var contentColumns = sheet.CellsUsed(XLCellsUsedOptions.Contents)
                .Where(cell => !IsEmptyCell(cell))
                .Select(cell => cell.Address.ColumnNumber).ToHashSet();
            if (p.ParseParameters.HeaderFormat.FormatType != HeaderFormatType.Nothing
                && sheet.FirstRowUsed(XLCellsUsedOptions.Contents) is { } firstRow)
            {
                contentColumns.ExceptWith(firstRow.CellsUsed(XLCellsUsedOptions.Contents)
                    .Where(cell => IsIrrelevantColumnHeader(cell.Value))
                    .Select(cell => cell.Address.ColumnNumber));
            }
            var studentNameColumn = p.ParseParameters.StudentNameColumn
                ?? FindStudentNameColumn(sheet, contentColumns, p.ParseParameters.HeaderFormat.FormatType);

            using var header = ParseHeaderInfo(
                sheet,
                headerDeps: new()
                {
                    LessonTypeParser = _lessonTypeParser,
                },
                p.ParseParameters.HeaderFormat,
                studentNameColumn,
                contentColumns);
            var additionallyIgnoredFields = new LessonModelDiffMask();
            switch (header.HeaderType)
            {
                case HeaderType.LessonType:
                {
                    additionallyIgnoredFields.LessonType = true;
                    break;
                }
                case HeaderType.None:
                case HeaderType.Date:
                {
                    break;
                }
                default:
                {
                    throw Unreachable();
                }
            }
            var lessonName = p.ParseParameters.WorksheetNames.TryGetValue(sheet.Name, out var mappedName)
                ? mappedName : sheet.Name;
            if (helper.LookupLessonByExcelName(lessonName, additionallyIgnoredFields, p.ParseParameters.LabMerges) is not { } lesson)
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
                    lessonType: header.HasDateLessonTypes ? LessonType.None : lesson.Type);
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
                        p: p.ParseParameters,
                        defaultLessonType: lesson.Type,
                        studentNameColumn: studentNameColumn,
                        contentColumns: contentColumns);
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
                if (Specializations.TryResolveSpecialization(
                    remapped.Value, specializationRegistry, out var specialization))
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
            LessonModelDiffMask additionallyIgnoredFields,
            IReadOnlyList<RegistryLabMergeRule> labMerges)
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
            var merge = key.LessonType == LessonType.Lab
                ? RegistryLabMerges.Find(labMerges, _schedule.Source, key.CourseId, key.Groups, key.GroupPartition)
                : null;
            var lookupKey = key;
            var lookupIgnoredFields = additionallyIgnoredFields;
            if (merge is not null)
            {
                lookupKey.GroupPartition = new(new SubGroup(merge.SourceSubGroup), Specialization.All);
                lookupIgnoredFields.LessonType = false;
            }
            var ret = LookupLesson(lookupKey, _schedule, lookupIgnoredFields);
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
                merge is null ? l.GroupPartitionKey : GroupPartitionKey.All,
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
        private readonly RentedBuffer<AttendanceDate> _dates;
        private readonly int[] _columns;
        public readonly ColumnOffset Offset { get; }
        public bool IsApplicable => HeaderType != HeaderType.None;
        public int Len { get; }
        public readonly HeaderType HeaderType = HeaderType.None;

        public ReadOnlySpan<LessonType> LessonTypes => _lessons.Span;
        public ReadOnlySpan<AttendanceDate> Dates => _dates.Span;
        public bool HasDateLessonTypes => HeaderType == HeaderType.Date && LessonTypes.ContainsAnyExcept(LessonType.None);

        public ParsedHeaderInfo(
            RentedBuffer<LessonType> lessons,
            RentedBuffer<AttendanceDate> dates,
            ColumnOffset offset,
            HeaderType headerType,
            int len,
            int[] columns)
        {
            _columns = columns;
            Len = len;
            _lessons = lessons;
            _dates = dates;
            Offset = offset;
            HeaderType = headerType;
        }

        public bool ContainsColumn(int column) => Array.IndexOf(_columns, column) >= 0;

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
            if (_dates.IsValid)
            {
                _dates.Dispose();
            }
        }
    }

    private enum HeaderType
    {
        None,
        LessonType,
        Date,
    }

    private struct ParsedHeaderInfoBuilder() : IDisposable
    {
        private const int UnspecifiedLen = -1;
        private HeaderType _headerType = HeaderType.None;
        private int _len = UnspecifiedLen;
        private RentedBuffer<LessonType> _lessons;
        private RentedBuffer<AttendanceDate> _dates;
        private int _count = 0;
        private readonly List<int> _columns = [];
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

        public void DeclareIsDate()
        {
            Debug.Assert(_headerType is HeaderType.None or HeaderType.Date);
            Debug.Assert(_len != UnspecifiedLen);
            if (_headerType == HeaderType.None)
            {
                _headerType = HeaderType.Date;
                _dates = new RentedBuffer<AttendanceDate>(_len);
                _lessons = new RentedBuffer<LessonType>(_len);
            }
        }

        public bool IsEmpty => _count == 0;

        public void AddLesson(LessonType t, int column)
        {
            DeclareIsLesson();
            int i = _count;
            Debug.Assert(i <= _len);
            _columns.Add(column);
            _lessons.Span[i] = t;
            _count++;
        }

        public void AddDate(AttendanceDate date, LessonType lessonType, int column)
        {
            DeclareIsDate();
            int i = _count;
            Debug.Assert(i <= _len);
            _columns.Add(column);
            _dates.Span[i] = date;
            _lessons.Span[i] = lessonType;
            _count++;
        }

        public void Dispose()
        {
            if (_lessons.IsValid)
            {
                _lessons.Dispose();
            }
            if (_dates.IsValid)
            {
                _dates.Dispose();
            }
        }

        public ParsedHeaderInfo Build(ColumnOffset columnOffset)
        {
            #if DEBUG
            Debug.Assert(!_built);
            _built = true;
            #endif
            var ret = new ParsedHeaderInfo(
                _lessons.IsValid ? _lessons.WithLen(_count) : default,
                _headerType == HeaderType.Date ? _dates.WithLen(_count) : default,
                columnOffset,
                _headerType,
                _count,
                _columns.ToArray());
            // Move into info so it can be disposed.
            _lessons = default;
            _dates = default;
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
        HeaderFormat headerFormat,
        int studentNameColumn,
        HashSet<int> contentColumns)
    {
        IXLRow? row = sheet.FirstRowUsed(XLCellsUsedOptions.Contents);
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

        Debug.Assert(headerFormat.FormatType is HeaderFormatType.LessonType or HeaderFormatType.Auto or HeaderFormatType.Date,
            $"Not implemented: {headerFormat.FormatType}");

        var context = new HeaderParsingContext(headerFormat.FormatType, studentNameColumn);
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
            if (!contentColumns.Contains(cell.Address.ColumnNumber)) continue;

            var action = HeaderValidateAndMaybeSkipEmptyOrValidateFormat(cell, ref context);
            if (action == HeaderColumnValidationResult.SkipEmpty)
            {
                continue;
            }
            if (action == HeaderColumnValidationResult.NotAHeader)
            {
                return default;
            }

            if (context.Format is HeaderFormatType.Auto or HeaderFormatType.Date
                && TryParseDateHeader(cell, headerDeps.LessonTypeParser, out var attendanceDate, out var dateLessonType))
            {
                builder.AddDate(attendanceDate, dateLessonType, cell.Address.ColumnNumber);
                context.Format = HeaderFormatType.Date;
                continue;
            }
            if (context.Format == HeaderFormatType.Date)
                throw cell.Exception($"Sheet '{sheet.Name}': expected a date (dd.MM or dd.MM.yyyy) in every attendance header column; got '{cell.Value}' ({cell.DataType})");

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
                builder.AddLesson(lessonType, cell.Address.ColumnNumber);
                return true;
            }
        }
        return builder.Build(context.FirstOffset ?? default);
    }

    private static bool TryParseDateHeader(
        IXLCell cell, LessonTypeParser lessonParser, out AttendanceDate date, out LessonType lessonType)
    {
        lessonType = LessonType.None;
        if (TryParseAttendanceDate(cell, out date)) return true;
        if (!cell.Value.TryGetText(out var text)) return false;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        for (int i = 0; i < 2; i++)
        {
            if (lessonParser.Parse(parts[1 - i]) is not { } parsedType) continue;
            if (!TryParseDateText(parts[i], out date)) continue;
            lessonType = parsedType;
            return true;
        }
        return false;
    }

    private static bool TryParseAttendanceDate(IXLCell cell, out AttendanceDate date)
    {
        if (cell.Value.IsDateTime)
        {
            var value = cell.Value.GetDateTime();
            date = new() { Month = value.Month, Day = value.Day, Year = value.Year };
            return true;
        }
        date = default;
        if (!cell.Value.TryGetText(out var text))
        {
            return false;
        }
        return TryParseDateText(text, out date);
    }

    private static bool TryParseDateText(string text, out AttendanceDate date)
    {
        date = default;
        text = text.Trim();
        if (text.Length == 0)
        {
            return false;
        }
        if (DateTime.TryParseExact(text,
                ["d.M", "dd.MM", "d.M.yyyy", "dd.MM.yyyy", "d.M.yy", "dd.MM.yy",
                    "d/M", "dd/MM", "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy",
                    "yyyy-MM-dd", "yyyy-M-d"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            // Year-less formats (d.M, dd.MM, d/M, dd/MM) parse with the current
            // year; treat them as year-less so they match by month/day.
            bool hasYear = text.Contains('-') || text.Count(c => c is '.' or '/') >= 2;
            date = new()
            {
                Month = parsed.Month,
                Day = parsed.Day,
                Year = hasYear ? parsed.Year : null,
            };
            return true;
        }
        return false;
    }

    // TODO: reuse the list
    private static void BuildList(
        IXLWorksheet sheet,
        StudentAttendanceListBuilder list,
        in ParsedHeaderInfo header,
        in WorksheetParseParameters p,
        LessonType defaultLessonType,
        int studentNameColumn,
        HashSet<int> contentColumns)
    {
        // ReSharper disable once GenericEnumeratorNotDisposed
        var firstRow = sheet.FirstRowUsed(XLCellsUsedOptions.Contents);
        if (firstRow is null) return;
        using var rowE = sheet.Rows().SkipWhile(row => row.RowNumber() < firstRow.RowNumber())
            .GetEnumerator().RememberIsDone();
        if (header.HeaderType != HeaderType.None || p.HeaderFormat.FormatType == HeaderFormatType.IgnoreHeader)
        {
            var x = rowE.MoveNext();
            Debug.Assert(x);
        }

        switch (header.HeaderType)
        {
            case HeaderType.LessonType:
            {
                if (p.DateMode == AttendanceDateMode.ValidateAndBind)
                {
                    throw new InvalidOperationException(
                        $"Sheet '{sheet.Name}': date validation requires a date header row, but the header holds lesson types.");
                }
                list.AddLessonTypes(header.LessonTypes);
                break;
            }
            case HeaderType.Date:
            {
                if (header.HasDateLessonTypes)
                {
                    var types = header.LessonTypes.ToArray();
                    for (int i = 0; i < types.Length; i++)
                        if (types[i] == LessonType.None) types[i] = defaultLessonType;
                    list.AddLessonTypes(types);
                }
                if (p.DateMode == AttendanceDateMode.ValidateAndBind)
                {
                    list.AddAttendanceDates(header.Dates);
                }
                break;
            }
            case HeaderType.None:
            {
                if (p.DateMode == AttendanceDateMode.ValidateAndBind)
                {
                    throw new InvalidOperationException(
                        $"Sheet '{sheet.Name}': date validation is enabled but no header row was recognized. Declare a date in every attendance column of the top row.");
                }
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

            var lastColumn = Math.Max(studentNameColumn, row.LastCellUsed()?.Address.ColumnNumber ?? studentNameColumn);
            using var cells = row.Cells(studentNameColumn, lastColumn).GetEnumerator();
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
                if ((header.IsApplicable || p.HeaderFormat.FormatType == HeaderFormatType.IgnoreHeader)
                    && !contentColumns.Contains(cells.Current!.Address.ColumnNumber)) continue;
                if (!cells.Current!.TryGetValue(out string attendanceStr))
                {
                    throw new InvalidOperationException("Expecting a string in cell");
                }

                if (header.IsApplicable)
                {
                    var columnNumber = cells.Current!.Address.ColumnNumber;
                    if (!header.ContainsColumn(columnNumber))
                    {
                        if (p.HeaderFormat.IgnoreValuesOutsideHeader)
                        {
                            continue;
                        }
                        throw cells.Current!.Exception("The given cell is not within the range of the table ");
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

        list.HintMaxCount(header.IsApplicable ? header.Len : maxLen);
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
        var isBlank = IsEmptyCell(cell);
        // Handle empty cells
        if (c.FirstOffset is null)
        {
            var columnNumber = cell.AsRange().FirstColumn().ColumnNumber();
            if (columnNumber < c.StudentNameColumn)
                return HeaderColumnValidationResult.SkipEmpty;
            if (columnNumber == c.StudentNameColumn)
            {
                if (!isBlank && !IsNameColumnHeader(value))
                {
                    if (c.Format == HeaderFormatType.Auto)
                    {
                        return HeaderColumnValidationResult.NotAHeader;
                    }
                    throw cell.Exception("First column of the header row must be empty or identify student names");
                }
                c.FirstOffset = new(columnNumber);
                c.PreviousOffset = new(columnNumber);
                return HeaderColumnValidationResult.SkipEmpty;
            }
            else if (columnNumber == c.StudentNameColumn + 1)
            {
                c.FirstOffset = new(columnNumber - 1);
                c.PreviousOffset = new(columnNumber - 1);
            }
        }

        if (c.IsInEmptyStreak)
        {
            if (!isBlank)
            {
                throw cell.Exception("Cannot have a non-empty value after an empty streak");
            }
            return HeaderColumnValidationResult.SkipEmpty;
        }
        else
        {
            if (isBlank)
            {
                c.IsInEmptyStreak = true;
                return HeaderColumnValidationResult.SkipEmpty;
            }
        }

        {
            // Some sanity checks.
            Debug.Assert(!c.IsInEmptyStreak);
            Debug.Assert(c.FirstOffset.HasValue);
            var colNumber = range.FirstColumn().ColumnNumber();
            Debug.Assert(colNumber > c.PreviousOffset.Value);
            c.PreviousOffset = new(colNumber);
        }
        return HeaderColumnValidationResult.Value;
    }

    private static int FindStudentNameColumn(
        IXLWorksheet sheet, HashSet<int> contentColumns, HeaderFormatType format)
    {
        if (format == HeaderFormatType.Nothing) return 1;
        var firstRow = sheet.FirstRowUsed(XLCellsUsedOptions.Contents);
        if (firstRow is null) return 1;
        foreach (var cell in firstRow.CellsUsed(XLCellsUsedOptions.Contents))
            if (IsNameColumnHeader(cell.Value)) return cell.Address.ColumnNumber;
        foreach (var column in contentColumns.Order())
        {
            if (IsIrrelevantColumnHeader(firstRow.Cell(column).Value)) continue;
            return column;
        }
        return 1;
    }

    private static bool IsIrrelevantColumnHeader(XLCellValue value)
    {
        return value.TryGetText(out var text) && text.Trim().ToLowerInvariant()
            is "nr." or "nr" or "no." or "no" or "№" or "#" or "număr" or "numar" or "number";
    }

    private static bool IsEmptyCell(IXLCell cell)
    {
        var value = cell.Value;
        return value.IsBlank || (value.TryGetText(out var text) && string.IsNullOrWhiteSpace(text))
            || IsGeneratedTableHeader(cell);
    }

    private static bool IsGeneratedTableHeader(IXLCell cell)
    {
        // ClosedXML fills empty Excel table header cells with ColumnN on load.
        if (!cell.Value.TryGetText(out var text) || !text.StartsWith("Column", StringComparison.Ordinal)
            || !int.TryParse(text.AsSpan(6), out var number)) return false;
        foreach (var table in cell.Worksheet.Tables)
        {
            if (table.RangeAddress.FirstAddress.RowNumber != cell.Address.RowNumber)
                continue;
            int fieldIndex = cell.Address.ColumnNumber - table.RangeAddress.FirstAddress.ColumnNumber;
            if (fieldIndex >= 0 && fieldIndex < table.Fields.Count()
                && number == fieldIndex + 1 && table.Field(fieldIndex).Name == text)
                return true;
        }
        return false;
    }

    private static bool IsNameColumnHeader(XLCellValue value)
    {
        if (!value.TryGetText(out var text))
            return false;
        return text.Trim().ToLowerInvariant() is "name" or "student" or "student name"
            or "nume" or "nume, prenume" or "nume prenume" or "nume și prenume" or "nume si prenume"
            or "фио" or "ф.и.о." or "имя";
    }

    private struct HeaderParsingContext(HeaderFormatType format, int studentNameColumn)
    {
        public int StudentNameColumn = studentNameColumn;
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
    Date,
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
    public int? StudentNameColumn = null;
    public IReadOnlyDictionary<string, string> WorksheetNames = new Dictionary<string, string>();
    public IReadOnlyList<RegistryLabMergeRule> LabMerges = [];
    public HeaderFormat HeaderFormat = new();
    public CellValueFormat CellValueFormat = CellValueFormat.NotAttendanceIsError;
    public RepeatedCourseBehavior RepeatedCourseBehavior = RepeatedCourseBehavior.Error;
    public AttendanceDateMode DateMode = AttendanceDateMode.Ignore;
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

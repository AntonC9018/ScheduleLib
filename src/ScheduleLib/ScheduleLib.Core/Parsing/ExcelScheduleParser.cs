using System.Diagnostics;
using System.Text;
using ClosedXML.Excel;
using ScheduleLib.Builders;
using ScheduleLib.Excel.Helper;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Application.Core.FR;

public static class ExcelScheduleParser
{
    public readonly struct Params
    {
        public required Stream InputFile { get; init; }
        public required DocParseContext Context { get; init; }
        public required StringBuilder StringBuilder { get; init; }

        public StringBuilder GetCleanStringBuilder()
        {
            StringBuilder.Clear();
            return StringBuilder;
        }
    }

    public static Config FrConfig => new()
    {
        AllowedAttendanceModes = new(AttendanceMode.FrecventaRedusa),
        AllowedQualificationTypes = new(QualificationType.Licenta),
        AddLessonHandler = FrScheduleAddLessonHandler.Instance,
        DayParseMode = DayParseMode.DayOfWeekAndDate,
    };
    public static Config MasterConfig => new()
    {
        AllowedAttendanceModes = new(AttendanceMode.Zi),
        AllowedQualificationTypes = new(QualificationType.Master),
        AddLessonHandler = RegularScheduleAddLessonHandler.Instance,
        DayParseMode = DayParseMode.DayOfWeek,
    };

    public static ValueTask ParseFrIntoSchedule(Params p)
    {
        return ParseIntoSchedule(FrConfig, p);
    }

    public static ValueTask ParseMasterIntoSchedule(Params p)
    {
        return ParseIntoSchedule(MasterConfig, p);
    }

    public sealed class Config
    {
        public required DayParseMode DayParseMode { get; init; }
        public required EnumBitArray<AttendanceMode> AllowedAttendanceModes { get; init; }
        public required EnumBitArray<QualificationType> AllowedQualificationTypes { get; init; }
        public required IScheduleAddLessonHandler AddLessonHandler { get; init; }
    }

    public static ValueTask ParseIntoSchedule(
        Config config,
        in Params p)
    {
        using var xl = new XLWorkbook(p.InputFile);
        var ws = xl.FindRelevantWorksheet();

        using var rowE = ws.Worksheet.Rows().GetEnumerator();
        if (!rowE.MoveNext())
        {
            throw new NotSupportedException("Expected header row!");
        }

        var previousResult = ParsingIterResult.None;
        bool isFirstIter = true;
        while (true)
        {
            var result = DoParsingIter(rowE, p, config, isFirstIter);
            if (result == ParsingIterResult.EndOfFile)
            {
                break;
            }
            if (result == ParsingIterResult.NothingAdded)
            {
                if (previousResult == ParsingIterResult.NothingAdded)
                {
                    break;
                }
            }
            previousResult = result;
            isFirstIter = false;
        }
        return ValueTask.CompletedTask;
    }

    private readonly record struct NumberedWorksheet(IXLWorksheet Worksheet, int Semester);

    private static IXLWorksheet FindRelevantWorksheet(this XLWorkbook xl)
    {
        var worksheets = xl.Worksheets;
        if (worksheets.Count == 1)
        {
            return worksheets.First();
        }
        var ret = xl.Worksheets
            .Select(x =>
            {
                var parser = new Parser(x.Name);
                if (!parser.ConsumeExactString("sem"))
                {
                    return default;
                }
                if (!parser.SkipWhitespace().SkippedAny)
                {
                    return default;
                }
                var roman = parser.ReadRoman();
                if (roman.Status != ReadRomanStatus.Ok)
                {
                    return default;
                }

                return new NumberedWorksheet(x, roman.Number);
            })
            // TODO: add validation for sem number?
            .WhereNotDefault()
            .Single()
            .Worksheet;
        return ret;
    }

    private enum ParsingIterResult
    {
        None,
        NothingAdded,
        EndOfFile,
        ProcessedNormally,
    }

    private static ParsingIterResult DoParsingIter(
        IEnumerator<IXLRow> rowE,
        in Params p,
        Config config,
        bool isFirstTime)
    {
        Span<GroupId> lessonGroupsMem = stackalloc GroupId[CellIterationContext.ColSpanHardLimit];
        if (ParseGrades(rowE) is not var (years, offset))
        {
            return ParsingIterResult.EndOfFile;
        }
        using var groups = ParseGroups(
            rowE,
            p.Context.Schedule,
            years,
            offset,
            config);

        if (isFirstTime)
        {
            if (!rowE.MoveNext())
            {
                throw new NotSupportedException("Expected a bullshit row!");
            }
        }

        var rowIterationContext = new RowIterationContext(
            startRowNumber: rowE.Current.RowNumber(),
            dayNameParser: p.Context.DayNameParser,
            timeConfig: p.Context.TimeConfig,
            dayParseMode: config.DayParseMode);
        var lessonParser = p.Context.ParserFactory.Create();
        var stringBuilder = p.GetCleanStringBuilder();
        var currentResult = ParsingIterResult.NothingAdded;

        while (true)
        {
            if (!rowE.MoveNext())
            {
                break;
            }
            var row = rowE.Current;
            var cellE = row.CellsWithMergedAppearingOnce().GetEnumerator();
            var action = rowIterationContext.Update(row, cellE);
            if (action == RowIterationContext.UpdateAction.Done)
            {
                break;
            }
            Debug.Assert(action == RowIterationContext.UpdateAction.Process);
            currentResult = ParsingIterResult.ProcessedNormally;

            var cellIterationContext = new CellIterationContext(cellE);

            while (true)
            {
                if (!cellIterationContext.Update())
                {
                    break;
                }

                var cell = cellIterationContext.Cell;
                var value = cell.Value;
                if (value.IsBlank)
                {
                    continue;
                }

                var lessonGroups = lessonGroupsMem[.. cellIterationContext.ColSpan];
                foreach (var (index, columnNumber) in cellIterationContext.ColumnNumbers.WithIndex())
                {
                    var offsetIndex = offset.GetUnOffsetIndex(columnNumber);
                    const string err = "Lesson found in a column that doesn't have a group assigned";
                    if (offsetIndex.Value < 0)
                    {
                        throw cell.Exception($"{err} (appearing BEFORE THE FIRST column with a group)");
                    }
                    if (offsetIndex.Value >= groups.Len)
                    {
                        throw cell.Exception($"{err} (appearing AFTER THE LAST column with a group)");
                    }
                    var groupId = groups.Get(offsetIndex);
                    lessonGroups[index] = groupId;
                }

                var text = value.GetText();
                using var textAsEnumerable = SingleItemEnumerator.Create(text.AsMemory());
                lessonParser.Lexer.Reset(textAsEnumerable);
                using var parsedLessonE = lessonParser.ParseLessons(stringBuilder).GetEnumerator();
                while (true)
                {
                    try
                    {
                        if (!parsedLessonE.MoveNext())
                        {
                            break;
                        }
                    }
                    catch (WrongFormatException exception)
                    {
                        throw cell.Exception("Error while parsing lesson", exception);
                    }
                    catch (Exception other)
                    {
                        throw cell.Exception("Error when adding lesson", other);
                    }

                    var parsedLesson = parsedLessonE.Current;
                    var err = config.AddLessonHandler.AddLesson(new(
                        context: p.Context,
                        parsedLesson: parsedLesson,
                        groups: lessonGroups,
                        timeSlot: rowIterationContext.TimeSlot,
                        day: rowIterationContext.Day));
                    if (err.ErrorString is { } errString)
                    {
                        throw cell.Exception(errString);
                    }
                }
            }
        }
        return currentResult;
    }

    private struct RowIterationContext(
        int startRowNumber,
        DayNameParser dayNameParser,
        LessonTimeConfig timeConfig,
        DayParseMode dayParseMode)
    {
        private Day _day = default;
        private int _currentRowSpan = 0;
        private int _rowNumber = startRowNumber;
        private int _rowsSinceLastDate = 0;
        private IXLRange? _lastDateRange = null;

        // the first expected number is I so it works out well.
        const int NoTimeSlotRoman = 0;
        private int _previousTimeSlotRoman = NoTimeSlotRoman;
        private TimeSlot? _timeSlot = null;

        public Day Day
        {
            get
            {
                Debug.Assert(_day != default);
                return _day;
            }
        }

        public TimeSlot TimeSlot
        {
            get
            {
                Debug.Assert(_timeSlot != null);
                return _timeSlot!.Value;
            }
        }

        public enum UpdateAction
        {
            Process,
            Done,
        }

        public UpdateAction Update(IXLRow row, IEnumerator<IXLCell> cellE)
        {
            {
                int newRowNumber = row.RowNumber();
                if (newRowNumber != _rowNumber + 1)
                {
                    throw row.Exception("No expected this row number.");
                }
                _rowNumber = newRowNumber;
            }

            if (!cellE.MoveNext())
            {
                throw new NotSupportedException("No date column found");
            }

            {
                var cell = cellE.Current;
                var range = cell.ActualRange();

                if (_rowsSinceLastDate == _currentRowSpan)
                {
                    if (range.Equals(_lastDateRange))
                    {
                        throw cell.Exception("Incorrectly computed range length?");
                    }

                    var value = cell.Value;
                    if (value.IsBlank)
                    {
                        return UpdateAction.Done;
                    }
                    var text = value.GetText();
                    if (text == "")
                    {
                        return UpdateAction.Done;
                    }

                    _currentRowSpan = range.RowCount();
                    _rowsSinceLastDate = 0;
                    var newDay = ParseDay(cell, text, dayNameParser, dayParseMode);
                    _lastDateRange = range;
                    _previousTimeSlotRoman = NoTimeSlotRoman;
                    _timeSlot = null;
                    _day = newDay;

                    if (newDay.Date.DayOfWeek != newDay.DayOfWeek)
                    {
                        throw cell.Exception("Wrong day of week specified in excel.");
                    }
                }
                else
                {
                    Debug.Assert(_lastDateRange != null);
                    if (!_lastDateRange.Equals(range))
                    {
                        throw cell.Exception("Expected ranges to have matched.");
                    }
                }
            }

            Debug.Assert(_day != default);
            _rowsSinceLastDate++;

            if (!cellE.MoveNext())
            {
                throw new NotSupportedException("No time slot cell");
            }

            {
                var cell = cellE.Current;
                if (cell.IsMerged())
                {
                    throw cell.Exception("Time slot cell must not be merged!");
                }

                var parser = new Parser(cell.GetString());
                parser.SkipWhitespace();
                var romanReadResult = parser.ReadRoman();
                if (romanReadResult.Status != ReadRomanStatus.Ok)
                {
                    if (_previousTimeSlotRoman != 0)
                    {
                        throw cell.Exception("Expected a roman numeral for the time slot.");
                    }
                }
                else
                {
                    if (romanReadResult.Number - _previousTimeSlotRoman != 1)
                    {
                        throw cell.Exception("Expected time slot roman numerals to be consecutive.");
                    }
                    _previousTimeSlotRoman = romanReadResult.Number;

                    if (!parser.SkipWhitespace().SkippedAny)
                    {
                        throw cell.Exception("Expected whitespace between roman time slot and ");
                    }
                }

                var timeInterval = parser.ParseTimeInterval();

                if (timeConfig.FindTimeSlotByStartTime(timeInterval.Start) is not { } timeSlotFound)
                {
                    throw cell.Exception($"Not found time slot with start time `{timeInterval.Start}`");
                }

                var newTimeSlotTime = timeConfig.GetTimeSlotInterval(timeSlotFound);
                if (newTimeSlotTime.End != timeInterval.End)
                {
                    throw cell.Exception($"The end time of interval `{timeInterval.End}` doesn't match.");
                }

                if (_timeSlot is { } timeSlot)
                {
                    int expectedNext = timeSlot.Index + 1;
                    if (expectedNext != timeSlotFound.Index)
                    {
                        throw cell.Exception("Time slot times must be consecutive.");
                    }
                }
                _timeSlot = timeSlotFound;
            }
            return UpdateAction.Process;
        }
    }

    private struct CellIterationContext(IEnumerator<IXLCell> cellE)
    {
        public const int ColSpanHardLimit = 64;
        private IEnumerator<IXLCell> _cellE = cellE;
        private int _colNumber = -1;
        private int _colSpan = 0;

        public IXLCell Cell
        {
            get
            {
                return _cellE.Current;
            }
        }

        public int ColSpan
        {
            get
            {
                Debug.Assert(_colSpan != 0);
                return _colSpan;
            }
        }

        public IEnumerable<int> ColumnNumbers
        {
            get
            {
                var range = _cellE.Current.ActualRange();
                var cols = range.Columns();
                var ret = cols.Select(x => x.ColumnNumber());
                return ret;
            }
        }

        public bool Update()
        {
            if (!_cellE.MoveNext())
            {
                return false;
            }
            var cell = _cellE.Current;
            var range = cell.ActualRange();
            if (range.RowCount() != 1)
            {
                throw cell.Exception("Cells spanning only a single row are allowed.");
            }

            var newColNumber = range.FirstColumn().ColumnNumber();
            if (_colNumber != -1)
            {
                int expectedNextCol = _colNumber + _colSpan;
                if (expectedNextCol != newColNumber)
                {
                    throw cell.Exception("Cells are not consecutive.");
                }
            }
            _colNumber = newColNumber;

            int colCount = range.ColumnCount();
            _colSpan = colCount;

            if (colCount > ColSpanHardLimit)
            {
                throw cell.Exception("Max columns hard limited to 64.");
            }
            return true;
        }
    }

    private static Day ParseDay(
        IXLCell context,
        string text,
        DayNameParser dayNameParser,
        DayParseMode parseMode)
    {
        var parser = new Parser(text);
        // DayOfWeek, dd.MM.yyyy
        if (parser.IsEmpty)
        {
            throw context.Exception("Expected cell to have the day");
        }
        var day = parser.ParseDayOfWeek(dayNameParser);
        if (parseMode == DayParseMode.DayOfWeek)
        {
            if (!parser.IsEmpty)
            {
                throw context.Exception("Expected the cell content to end after the day of week");
            }
            return new Day(day, default);
        }
        Debug.Assert(parseMode == DayParseMode.DayOfWeekAndDate);
        if (!parser.ConsumeExactChar(','))
        {
            throw context.Exception("Expected ',' after the day name");
        }
        parser.SkipWhitespace();
        var date = parser.ParseDate("dd.MM.yyyy");
        parser.SkipWhitespace();
        if (!parser.IsEmpty)
        {
            throw context.Exception("Not parsed the input string fully");
        }
        return new(day, date);
    }

    private readonly record struct Grades
    {
        private readonly SizedItemArray<Grade> _array;

        public Grades(SizedItemArray<Grade> array)
        {
            _array = array;
        }

        public int Count => _array.TotalSize;

        public Grade Get(RestoredIndex index)
        {
            return _array.Find(index.Value);
        }
    }

    private static (Grades Grades, ColumnOffset Offset)? ParseGrades(IEnumerator<IXLRow> rowE)
    {
        var ret = new SizedItemArray<Grade>();
        if (!rowE.MoveNext())
        {
            return null;
        }

        int? startOffset = null;

        foreach (var cell in rowE.Current.CellsWithMergedAppearingOnce())
        {
            var range = cell.ActualRange();
            if (range.RowCount() != 1)
            {
                if (startOffset != null)
                {
                    throw cell.Exception("Multirow header column after non-empty cell");
                }
                continue;
            }

            var str = cell.GetString();
            if (str is null or "")
            {
                if (startOffset != null)
                {
                    break;
                }
                continue;
            }

            var parser = new Parser(str);
            if (!parser.ConsumeExactString("Anul"))
            {
                if (startOffset != null)
                {
                    throw cell.Exception("Expected `Anul` in the header row.");
                }
                continue;
            }
            if (startOffset == null)
            {
                startOffset = range.FirstColumn().ColumnNumber();
            }

            if (!parser.SkipWhitespace().SkippedAny)
            {
                throw cell.Exception("Expected whitespace after `Anul`.");
            }
            var romanResult = parser.ReadRoman();
            if (romanResult.Status != ReadRomanStatus.Ok)
            {
                throw cell.Exception("Expected roman after `Anul`.");
            }

            var grade = romanResult.Number;
            var columnNumber = range.FirstColumn().ColumnNumber();
            var columnCount = cell.ColumnCount();
            int index = columnNumber - startOffset.Value;

            var item = new SizedItem<Grade>(item: new(grade), size: columnCount);
            if (ret.TotalSize != index)
            {
                throw cell.Exception("Empty grade cell not allowed.");
            }
            ret.Add(item);
        }
        return (new(ret), new(startOffset ?? 0));
    }

    private readonly struct Groups : IDisposable
    {
        private readonly RentedBuffer<GroupId> _arr;

        public Groups(RentedBuffer<GroupId> arr)
        {
            _arr = arr;
        }

        public GroupId Get(RestoredIndex index)
        {
            Debug.Assert(index.Value < _arr.Span.Length);
            return _arr.Span[index.Value];
        }

        public int Len => _arr.Len;

        public void Dispose()
        {
            if (_arr.IsValid)
            {
                _arr.Dispose();
            }
        }
    }

    private static Groups ParseGroups(
        IEnumerator<IXLRow> rowE,
        ScheduleBuilder builder,
        Grades grades,
        ColumnOffset offset,
        Config config)
    {
        if (!rowE.MoveNext())
        {
            throw new NotSupportedException("No groups row found.");
        }
        var outputCount = grades.Count;
        if (outputCount == 0)
        {
            return default;
        }
        using var cellE = rowE.Current.CellsWithMergedAppearingOnce().GetEnumerator();
        while (true)
        {
            if (!cellE.MoveNext())
            {
                return default;
            }
            var cell = cellE.Current;
            var col = cell.AsRange().FirstColumn().ColumnNumber();
            var colIndex = offset.GetUnOffsetIndex(col);
            if (colIndex.Value == 0)
            {
                break;
            }
            if (colIndex.Value > 0)
            {
                throw cell.Exception("Number of blanks doesn't match");
            }
            if (!cell.Value.IsBlank)
            {
                throw cell.Exception("Skipped cells must be blank");
            }
        }

        var groups = new RentedBuffer<GroupId>(outputCount);
        try
        {
            int groupIndex = 0;
            while (true)
            {
                var cell = cellE.Current;
                var col = cell.AsRange().FirstColumn().ColumnNumber();
                var colIndex = offset.GetUnOffsetIndex(col);
                if (colIndex.Value != groupIndex)
                {
                    throw cell.Exception("Skipped a column, they must be consecutive.");
                }

                var str = cell.GetString();
                if (!string.IsNullOrEmpty(str))
                {
                    if (colIndex.Value >= outputCount)
                    {
                        throw cell.Exception("Groups beyond the grade ranges are not allowed.");
                    }
                }
                else
                {
                    break;
                }

                var group = builder.Group(str);
                if (!config.AllowedAttendanceModes.Contains(group.Ref.AttendanceMode))
                {
                    throw cell.Exception($"Expected only {config.AllowedAttendanceModes} groups in the excel");
                }
                if (!config.AllowedQualificationTypes.Contains(group.Ref.QualificationType))
                {
                    throw cell.Exception($"Expected only {config.AllowedQualificationTypes} groups in the excel");
                }

                var grade = grades.Get(colIndex);
                if (group.Ref.Grade != grade)
                {
                    throw cell.Exception("Grade parsed vs specified mismatch");
                }

                groups.Span[groupIndex] = group;
                groupIndex++;

                if (!cellE.MoveNext())
                {
                    break;
                }
            }

            if (groupIndex != outputCount)
            {
                throw new NotSupportedException("Not all columns covered by year are covered by groups");
            }
        }
        catch
        {
            groups.Dispose();
            throw;
        }

        return new(groups);
    }
}

public enum DayParseMode
{
    DayOfWeek,
    DayOfWeekAndDate,
}

public readonly record struct Day(
    DayOfWeek DayOfWeek,
    DateOnly Date);

public readonly record struct AddLessonResult(string? ErrorString)
{
    public static AddLessonResult Ok => new(null);
    public static AddLessonResult Error(string err) => new(err);
}

public readonly ref struct AddLessonParams
{
    public readonly DocParseContext Context;
    public readonly ref readonly ParsedLesson ParsedLesson;
    public readonly ReadOnlySpan<GroupId> Groups;
    public readonly TimeSlot TimeSlot;
    public readonly Day Day;

    public AddLessonParams(
        DocParseContext context,
        in ParsedLesson parsedLesson,
        ReadOnlySpan<GroupId> groups,
        TimeSlot timeSlot,
        Day day)
    {
        Context = context;
        Groups = groups;
        TimeSlot = timeSlot;
        Day = day;
        ParsedLesson = ref parsedLesson;
    }
}

public interface IScheduleAddLessonHandler
{
    public AddLessonResult AddLesson(AddLessonParams p);
}

public sealed class FrScheduleAddLessonHandler : IScheduleAddLessonHandler
{
    public static readonly FrScheduleAddLessonHandler Instance = new();

    public AddLessonResult AddLesson(AddLessonParams p)
    {
        // We don't record these in the schedule.
        if (p.ParsedLesson.LessonType == LessonType.Exam)
        {
            return AddLessonResult.Ok;
        }

        if (p.ParsedLesson.Parity != Parity.EveryWeek)
        {
            return AddLessonResult.Error("Parity not supported for FR.");
        }
        if (p.ParsedLesson.StartTime != null)
        {
            return AddLessonResult.Error("Different start time not supported for FR.");
        }

        var builder = p.Context.Schedule.OneTimeLesson();

        var subGroupStatus = p.Context.SetCommonProps(builder, p.ParsedLesson);
        if (subGroupStatus != SubGroupStatus.GroupNameIsSubGroup)
        {
            if (!p.ParsedLesson.GroupName.IsEmpty)
            {
                return AddLessonResult.Error("A different group name in FR is not allowed.");
            }
        }

        builder.Groups(p.Groups);
        builder.TimeSlot(p.TimeSlot);
        builder.Date(p.Day.Date);
        return AddLessonResult.Ok;
    }
}

public sealed class RegularScheduleAddLessonHandler : IScheduleAddLessonHandler
{
    public static readonly RegularScheduleAddLessonHandler Instance = new();

    public AddLessonResult AddLesson(AddLessonParams p)
    {
        var allowedTypes = EnumBitArray<LessonType>.From(
            LessonType.Lab,
            LessonType.Prelegere,
            LessonType.Curs);

        if (!allowedTypes.Contains(p.ParsedLesson.LessonType))
        {
            return AddLessonResult.Error($"Only {allowedTypes} lesson types are supported");
        }

        if (p.ParsedLesson.StartTime != null)
        {
            return AddLessonResult.Error("Different start time not supported.");
        }

        var builder = p.Context.Schedule.RegularLesson();
        _ = p.Context.SetCommonProps(builder, p.ParsedLesson);
        builder.Groups(p.Groups);
        builder.TimeSlot(p.TimeSlot);
        builder.Parity(p.ParsedLesson.Parity);
        builder.DayOfWeek(p.Day.DayOfWeek);
        return AddLessonResult.Ok;
    }
}


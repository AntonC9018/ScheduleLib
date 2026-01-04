using System.Diagnostics;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.WordDoc;

namespace MainCli.FR;

public static class FrExcelParser
{
    public struct Params
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

    private readonly record struct NumberedWorksheet(IXLWorksheet Worksheet, int Semester);

    public static ValueTask Parse(Params p)
    {
        using var xl = new XLWorkbook(p.InputFile);
        var ws = xl.Worksheets
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
            .WhereNotDefault()
            .Single();

        using var rowE = ws.Worksheet.Rows().GetEnumerator();
        if (!rowE.MoveNext())
        {
            throw new NotSupportedException("Expected header row!");
        }
        var years = ParseGrades(rowE);
        var groups = ParseGroups(rowE, p.Context.Schedule, years);
        if (!rowE.MoveNext())
        {
            throw new NotSupportedException("Bullshit row not found");
        }

        var rowIterationContext = new RowIterationContext(
            startRowNumber: rowE.Current!.RowNumber(),
            dayNameParser: p.Context.DayNameParser,
            timeConfig: p.Context.TimeConfig);
        var lessonParser = p.Context.ParserFactory.Create();
        var stringBuilder = p.GetCleanStringBuilder();
        Span<GroupId> lessonGroupsMem = stackalloc GroupId[CellIterationContext.ColSpanHardLimit];

        while (true)
        {
            if (!rowE.MoveNext())
            {
                break;
            }
            var row = rowE.Current!;
            var cellE = row.Cells().GetEnumerator();
            rowIterationContext.Update(row, cellE);

            var cellIterationContext = new CellIterationContext(cellE);

            while (true)
            {
                if (!cellIterationContext.Update())
                {
                    break;
                }

                var lessonGroups = lessonGroupsMem[.. cellIterationContext.ColSpan];
                foreach (var (index, columnNumber) in cellIterationContext.ColumnNumbers.WithIndex())
                {
                    var groupId = groups.Get(columnNumber);
                    lessonGroups[index] = groupId;
                }

                var text = cellIterationContext.Cell.GetText();
                using var textAsEnumerable = SingleItemEnumerator.Create(text);
                lessonParser.Lexer.Reset(textAsEnumerable);
                var parsedLessons = lessonParser.ParseLessons(stringBuilder);
                foreach (var parsedLesson in parsedLessons)
                {
                    if (parsedLesson.Parity != Parity.EveryWeek)
                    {
                        throw new NotSupportedException("Parity not supported for FR.");
                    }
                    if (parsedLesson.StartTime != null)
                    {
                        throw new NotSupportedException("Different start time not supported for FR.");
                    }

                    var builder = p.Context.Schedule.OneTimeLesson();

                    var subGroupStatus = p.Context.SetCommonProps(builder, parsedLesson);
                    if (subGroupStatus != SubGroupStatus.GroupNameIsSubGroup)
                    {
                        if (!parsedLesson.GroupName.IsEmpty)
                        {
                            throw new NotSupportedException("A different group name in FR is not allowed.");
                        }
                    }

                    builder.Groups(lessonGroups);
                    builder.TimeSlot(rowIterationContext.TimeSlot);
                    builder.Date(rowIterationContext.Day.Date);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private struct RowIterationContext(
        int startRowNumber,
        DayNameParser dayNameParser,
        LessonTimeConfig timeConfig)
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

        public void Update(IXLRow row, IEnumerator<IXLCell> cellE)
        {
            {
                int newRowNumber = row.RowNumber();
                if (newRowNumber != _rowNumber + 1)
                {
                    throw new NotSupportedException("No expected this row number.");
                }
                _rowNumber = newRowNumber;
            }

            if (!cellE.MoveNext())
            {
                throw new NotSupportedException("No date column found");
            }

            var range = cellE.Current.MergedRange();

            if (_rowsSinceLastDate == _currentRowSpan)
            {
                if (range.Equals(_lastDateRange))
                {
                    throw new NotSupportedException("Incorrectly computed range length?");
                }

                _currentRowSpan = range.RowCount();
                _rowsSinceLastDate = 0;
                var cell = cellE.Current;
                var text = cell.GetText();
                var newDay = ParseDay(text, dayNameParser);
                _lastDateRange = range;
                _previousTimeSlotRoman = NoTimeSlotRoman;
                _day = newDay;

                if (newDay.Date.DayOfWeek != newDay.DayOfWeek)
                {
                    throw new InvalidOperationException("Wrong day of week specified in excel.");
                }
            }
            else
            {
                Debug.Assert(_lastDateRange != null);
                if (!_lastDateRange.Equals(range))
                {
                    throw new NotSupportedException("Expected ranges to have matched.");
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
                    throw new NotSupportedException("Time slot cell must not be merged!");
                }

                var parser = new Parser(cell.GetString());
                var romanReadResult = parser.ReadRoman();
                if (romanReadResult.Status != ReadRomanStatus.Ok)
                {
                    throw new NotSupportedException("Expected a roman numeral for the time slot.");
                }
                if (romanReadResult.Number - _previousTimeSlotRoman != 1)
                {
                    throw new NotSupportedException("Expected time slot roman numerals to be consecutive.");
                }
                _previousTimeSlotRoman = romanReadResult.Number;

                if (!parser.SkipWhitespace().SkippedAny)
                {
                    throw new NotSupportedException("Expected whitespace between roman time slot and ");
                }
                var timeInterval = parser.ParseTimeInterval();

                if (timeConfig.FindTimeSlotByStartTime(timeInterval.Start) is not { } timeSlotFound)
                {
                    throw new NotSupportedException($"Not found time slot with start time `{timeInterval.Start}`");
                }

                var newTimeSlotTime = timeConfig.GetTimeSlotInterval(timeSlotFound);
                if (newTimeSlotTime.End != timeInterval.End)
                {
                    throw new NotSupportedException($"The end time of interval `{timeInterval.End}` doesn't match.");
                }

                if (_timeSlot is { } timeSlot)
                {
                    int expectedNext = timeSlot.Index + 1;
                    if (expectedNext != timeSlotFound.Index)
                    {
                        throw new NotSupportedException("Time slot times must be consecutive.");
                    }

                    _timeSlot = timeSlotFound;
                }
            }
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
                var range = _cellE.Current.MergedRange();
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
            var merged = cell.MergedRange();
            if (merged.RowCount() != 1)
            {
                throw new NotSupportedException("Cells spanning only a single row are allowed.");
            }

            var newColNumber = cell.AsRange().FirstColumn().ColumnNumber();
            if (_colNumber != -1)
            {
                int expectedNextCol = _colNumber + _colSpan;
                if (expectedNextCol != newColNumber)
                {
                    throw new NotSupportedException("Cells are not consecutive.");
                }
            }
            _colNumber = newColNumber;

            int colCount = merged.ColumnCount();
            _colSpan = colCount;

            if (colCount > ColSpanHardLimit)
            {
                throw new NotSupportedException("Max columns hard limited to 64.");
            }
            return true;
        }
    }

    private static Day ParseDay(string text, DayNameParser dayNameParser)
    {
        var parser = new Parser(text);
        // DayOfWeek, dd.MM.yyyy
        if (parser.IsEmpty)
        {
            throw new NotSupportedException("Expected cell to have the date");
        }
        var day = parser.ParseDayOfWeek(dayNameParser);
        if (!parser.ConsumeExactChar(','))
        {
            throw new NotSupportedException("Expected ',' after the day name");
        }
        parser.SkipWhitespace();
        var date = parser.ParseDate("dd.MM.yyyy");
        parser.SkipWhitespace();
        if (!parser.IsEmpty)
        {
            throw new NotSupportedException("Not parsed the input string fully");
        }
        return new(day, date);
    }


    private readonly record struct Day(DayOfWeek DayOfWeek, DateOnly Date);

    private const int NoYear = -1;
    private static SizedItemArray<int> ParseGrades(IEnumerator<IXLRow> rowE)
    {
        var ret = new SizedItemArray<int>();
        if (!rowE.MoveNext())
        {
            throw new NotSupportedException("Expected grade row!");
        }
        foreach (var cell in rowE.Current.Cells())
        {
            var merged = cell.MergedRange();
            if (merged.Rows().Count() != 1)
            {
                continue;
            }

            var str = cell.GetString();
            var parser = new Parser(str);
            if (!parser.ConsumeExactString("Anul"))
            {
                throw new NotSupportedException("Expected `Anul` in the header row.");
            }
            if (!parser.SkipWhitespace().SkippedAny)
            {
                throw new NotSupportedException("Expected whitespace after `Anul`.");
            }
            var romanResult = parser.ReadRoman();
            if (romanResult.Status != ReadRomanStatus.Ok)
            {
                throw new NotSupportedException("Expected roman after `Anul`.");
            }

            var grade = romanResult.Number;
            var columnNumber = cell.AsRange().FirstColumn().ColumnNumber();
            var columnCount = merged.ColumnCount();

            var item = new SizedItem<int>(item: grade, size: columnCount);
            if (ret.IsEmpty)
            {
                ret.AddAt(columnNumber, item, fillerValue: NoYear);
            }
            else
            {
                if (ret.TotalSize != columnNumber)
                {
                    throw new NotSupportedException("Empty grade cell not allowed.");
                }
                ret.Add(item);
            }
        }
        return ret;
    }

    private readonly struct Groups
    {
        private readonly GroupId[] _arr;
        private readonly int _firstColumnOffset;

        public Groups(GroupId[] arr, int firstColumnOffset)
        {
            _arr = arr;
            _firstColumnOffset = firstColumnOffset;
        }

        public int FirstColumn() => _firstColumnOffset;

        public GroupId Get(int column)
        {
            int index = column - _firstColumnOffset;
            return _arr[index];
        }
    }

    private static Groups ParseGroups(
        IEnumerator<IXLRow> rowE,
        ScheduleBuilder builder,
        SizedItemArray<int> years)
    {
        if (!rowE.MoveNext())
        {
            throw new NotSupportedException("No groups row found.");
        }
        if (years.FindPositionOfFirstOtherThan(NoYear) is not { } firstColumnOffset)
        {
            return new([], 0);
        }
        var outputCount = years.Count - firstColumnOffset;
        using var cellE = rowE.Current.Cells().GetEnumerator();
        while (true)
        {
            if (!cellE.MoveNext())
            {
                return new([], firstColumnOffset);
            }
            var col = cellE.Current!.AsRange().FirstColumn().ColumnNumber();
            if (col == outputCount)
            {
                break;
            }
            if (col > outputCount)
            {
                throw new NotSupportedException("Number of blanks doesn't match");
            }
            if (!cellE.Current!.Value.IsBlank)
            {
                throw new NotSupportedException("Skipped cells must be blank");
            }
        }

        var groups = new GroupId[outputCount];
        int groupIndex = 0;
        while (true)
        {
            var cell = cellE.Current!;
            var str = cell.GetString();
            var group = builder.Group(str);
            if (group.Ref.AttendanceMode != AttendanceMode.FrecventaRedusa)
            {
                throw new NotSupportedException("Expected only FR groups in the FR excel");
            }

            {
                var col = cell.AsRange().FirstColumn().ColumnNumber();
                var colIndex = col - firstColumnOffset;
                if (colIndex != groupIndex)
                {
                    throw new NotSupportedException("Skipped a column, they must be consecutive.");
                }
            }

            groups[groupIndex] = group;
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

        return new(groups, firstColumnOffset);
    }
}

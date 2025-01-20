using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using App;
using App.Generation;
using App.Parsing.Lesson;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Table = DocumentFormat.OpenXml.Wordprocessing.Table;


var c = DocParseContext.Create(new()
{
    DayNameProvider = new(),
});
c.Schedule.SetStudyYear(2024);

const string fileName = "Orar_An_II Lic.docx";
var fullPath = Path.GetFullPath(fileName);
using var doc = WordprocessingDocument.Open(fullPath, isEditable: false);
if (doc.MainDocumentPart?.Document.Body is not { } bodyElement)
{
    return;
}

// Multirow cell has <w:vMerge w:val="restart"/> on the first cell, and an empty <w:vMerge /> on the next cells
// Multicolumn cell has <w:gridSpan w:val="2" /> where 2 indicates the column size
// May be combined
var tables = bodyElement.ChildElements
    .OfType<Table>()
    .ToArray();
int colOffset = 0;
var state = new TableParsingState();

foreach (var table in tables)
{
    var rows = table.ChildElements.OfType<TableRow>();
    using var rowEnumerator = rows.GetEnumerator();
    if (!rowEnumerator.MoveNext())
    {
        break;
    }

    IEnumerable<TableCell> Cells() => rowEnumerator.Current.ChildElements.OfType<TableCell>();

    {
        const int expectedHeaderWidth = 2;
        var result = ParseHeaderRow(colOffset, expectedSkippedCount: expectedHeaderWidth);
        switch (result.Status)
        {
            case HeaderRowResultStatus.Nothing:
            {
                // ??
                break;
            }
            case HeaderRowResultStatus.NoData:
            {
                throw new NotSupportedException("Empty table not supported");
            }
            case HeaderRowResultStatus.WrongAmountSkipped:
            {
                if (result.SkippedCount != 0)
                {
                    throw new NotSupportedException($"Expected {expectedHeaderWidth} empty columns for the left header");
                }
                if (state.ColumnCounts is null)
                {
                    throw new NotSupportedException("Parsing must begin with a table that has the header with the groups");
                }
                break;
            }
            case HeaderRowResultStatus.GroupNames:
            {
                state.ColumnCounts = new(expectedHeaderWidth, result.GroupCount);

                // Table with no header is allowed ig.
                if (!rowEnumerator.MoveNext())
                {
                    continue;
                }
                break;
            }
            default:
            {
                Debug.Fail("Impossible");
                throw new NotImplementedException("??");
            }
        }
    }

    const int dayColumnIndex = 0;
    const int timeSlotColumnIndex = 1;

    while (true)
    {
        int columnIndex = 0;
        foreach (var cell in Cells())
        {
            var props = cell.TableCellProperties;
            var colSpan = props?.GridSpan?.Val ?? 1;

            switch (columnIndex)
            {
                case dayColumnIndex:
                {
                    var newDay = DayOfWeekCol(state.CurrentDay);
                    if (newDay != state.CurrentDay)
                    {
                        state.CurrentDay = newDay;
                        state.Time = null;
                    }
                    break;
                }
                case timeSlotColumnIndex:
                {
                    state.Time = TimeSlotCol(state.Time);
                    break;
                }
                default:
                {
                    NormalCol(colSpan);
                    break;
                }
            }

            switch (columnIndex)
            {
                case dayColumnIndex or timeSlotColumnIndex:
                {
                    if (colSpan != 1)
                    {
                        throw new NotSupportedException("The day and time slot columns must be one column in width");
                    }
                    break;
                }
            }

            int countLeft = state.ColumnCounts!.Value.Total - columnIndex;
            if (countLeft < colSpan)
            {
                throw new NotSupportedException("The column count is off");
            }
            columnIndex += colSpan;

            DayOfWeek DayOfWeekCol(DayOfWeek? currentDay)
            {
                if (props?.VerticalMerge is not { } mergeStart)
                {
                    throw new NotSupportedException("Invalid format");
                }

                if (mergeStart.Val is not { } mergeStartVal
                    || mergeStartVal == MergedCellValues.Continue)
                {
                    if (currentDay is { } v)
                    {
                        return v;
                    }
                    throw new NotSupportedException("Expected the day");
                }

                if (mergeStart.Val != MergedCellValues.Restart)
                {
                    throw new NotSupportedException($"Unsupported merge cell command: {mergeStart.Val}");
                }

                if (cell.InnerText is not { } dayNameText)
                {
                    throw new NotSupportedException("The day name column must include the day name");
                }

                if (c.DayNameParser.Map(dayNameText) is not { } day)
                {
                    throw new NotSupportedException($"The day {dayNameText} is invalid");
                }

                return day;
            }

            TimeParsingState TimeSlotCol(TimeParsingState? currentTime)
            {
                {
                    if (props?.VerticalMerge is { } mergeStart)
                    {
                        if (mergeStart.Val is not { } mergeStartVal
                            || mergeStartVal == MergedCellValues.Continue)
                        {
                            if (currentTime is not { } v)
                            {
                                throw new NotSupportedException("Expected the time slot");
                            }
                            return v;
                        }

                        if (mergeStart.Val != MergedCellValues.Restart)
                        {
                            throw new NotSupportedException($"Unsupported merge cell command: {mergeStart.Val}");
                        }
                    }
                }

                // I
                // 8:00-9:30

                // Two paragraphs
                using var paragraphs = cell.ChildElements.OfType<Paragraph>().GetEnumerator();
                if (!paragraphs.MoveNext())
                {
                    throw new NotSupportedException("Invalid time slot cell");
                }

                int newTimeSlotOrdinal;
                {
                    var numberParagraph = paragraphs.Current;
                    if (numberParagraph.InnerText is not { } numberText)
                    {
                        throw new NotSupportedException("The time slot must contain the ordinal first");
                    }

                    var maybeNum = NumberHelper.FromRoman(numberText);
                    if (maybeNum is not { } num)
                    {
                        throw new NotSupportedException("The time slot number should be a roman numeral");
                    }
                    int currentOrdinal = currentTime?.TimeSlotOrdinal ?? 0;
                    if (num != currentOrdinal + 1)
                    {
                        throw new NotSupportedException("The time slot number must be in order");
                    }

                    newTimeSlotOrdinal = num;
                }

                if (!paragraphs.MoveNext())
                {
                    throw new NotSupportedException("");
                }

                var parsedTime = Time();

                if (paragraphs.MoveNext())
                {
                    throw new NotSupportedException("Extra paragraphs");
                }

                {
                    var timeStarts = c.TimeConfig.TimeSlotStarts;
                    int timeSlotIndex = NextTimeSlotIndex();
                    if (timeStarts[timeSlotIndex] != parsedTime.Start)
                    {
                        throw new NotSupportedException("The time slots must follow each other");
                    }

                    var expectedEndTime = parsedTime.Start.Add(c.TimeConfig.LessonDuration);
                    if (expectedEndTime != parsedTime.End)
                    {
                        throw new NotSupportedException($"The lesson durations must all be equal to the default duration ({c.TimeConfig.LessonDuration} minutes)");
                    }

                    return new()
                    {
                        TimeSlot = new(timeSlotIndex),
                        TimeSlotOrdinal = newTimeSlotOrdinal,
                    };
                }

                (TimeOnly Start, TimeOnly End) Time()
                {
                    var timeParagraph = paragraphs.Current;
                    if (timeParagraph.InnerText is not { } timeText)
                    {
                        TimeSlotError();
                    }

                    // HH:MM-HH:MM
                    var parser = new Parser(timeText);
                    parser.SkipWhitespace();
                    if (ParserHelper.ParseTime(ref parser) is not { } startTime)
                    {
                        throw new NotSupportedException("Expected time range start");
                    }
                    if (parser.IsEmpty || parser.Current != '-')
                    {
                        throw new NotSupportedException("Expected '-' after start time");
                    }
                    parser.Move();

                    if (ParserHelper.ParseTime(ref parser) is not { } endTime)
                    {
                        throw new NotSupportedException("Expected time range end");
                    }

                    parser.SkipWhitespace();

                    if (!parser.IsEmpty)
                    {
                        throw new NotSupportedException("Time range not consumed fully");
                    }

                    return (startTime, endTime);
                }

                int NextTimeSlotIndex()
                {
                    if (currentTime is { } x)
                    {
                        return x.TimeSlot.Index + 1;
                    }

                    int ret = FindTimeSlotIndex(parsedTime.Start);
                    return ret;
                }
            }

            void NormalCol(int colSpan1)
            {
                if (!ShouldAdd())
                {
                    return;
                }

                // 15:00 Baze de date(curs)
                // L.Novac    404/4
                // (optional) time override
                // name
                // (optional) (type,parity) or (parity) or (type)  -- A
                // (optional) second name + A
                // (optional) roman subgroup:    --  I:
                // professor name
                // cab (optional?)

                // First organize as just lines, it's easier
                var lines = cell.ChildElements
                    .OfType<Paragraph>()
                    .SelectMany(x => x.InnerText.Split("\n"));
                var lessons = LessonParsingHelper.ParseLessons(new()
                {
                    Lines = lines,
                    ParityParser = ParityParser.Instance,
                    LessonTypeParser = LessonTypeParser.Instance,
                });

                foreach (var lesson in lessons)
                {
                    var l = c.Schedule.RegularLesson();

                    if (lesson.StartTime is { } startTime)
                    {
                        int timeSlotIndex = FindTimeSlotIndex(startTime);
                        l.TimeSlot(new(timeSlotIndex));
                    }

                    l.DayOfWeek(state.CurrentDay!.Value);

                    // TODO: unite names
                    {
                        var courseName = lesson.LessonName.ToString();
                        var courseId = c.Course(courseName);
                        l.Course(courseId);
                    }
                    {
                        var teacherName = lesson.TeacherName.ToString();
                        var teacherId = c.Teacher(teacherName);
                        l.Teacher(teacherId);
                    }
                    {
                        var roomName = lesson.RoomName.ToString();
                        var roomId = c.Room(roomName);
                        l.Room(roomId);
                    }
                    l.Type(lesson.LessonType);
                    l.Parity(lesson.Parity);

                    {
                        var groups = new LessonGroups();
                        for (int i = 0; i < colSpan1; i++)
                        {
                            var groupId = GroupId(i + columnIndex);
                            groups.Add(groupId);
                        }

                        if (groups.Count > 1
                            && lesson.SubGroupNumber != SubGroupNumber.All)
                        {
                            throw new NotSupportedException("Lessons with subgroups with multiple groups not supported");
                        }

                        ref var g = ref l.Model.Group;
                        g.Groups = groups;
                        g.SubGroup = lesson.SubGroupNumber;
                    }
                }

                bool ShouldAdd()
                {
                    if (props?.VerticalMerge is not { } merge)
                    {
                        return true;
                    }
                    if (merge.Val is { } val
                        && val == MergedCellValues.Restart)
                    {
                        return true;
                    }
                    return false;
                }
            }
        }

        if (!rowEnumerator.MoveNext())
        {
            break;
        }
    }
    continue;

    int FindTimeSlotIndex(TimeOnly start)
    {
        int i = Array.BinarySearch(c.TimeConfig.TimeSlotStarts, start);
        if (i == -1)
        {
            TimeSlotError();
        }
        return i;
    }

    GroupId GroupId(int colIndex)
    {
        return new(colOffset + colIndex - state.ColumnCounts!.Value.Skipped);
    }

    HeaderRowResult ParseHeaderRow(int colOffset1, int expectedSkippedCount)
    {
        int skippedCount = 0;
        int goodCellCount = 0;
        using var cellEnumerator = Cells().GetEnumerator();
        if (!cellEnumerator.MoveNext())
        {
            return HeaderRowResult.Nothing;
        }
        while (true)
        {
            var text = cellEnumerator.Current.InnerText;
            if (text != "")
            {
                break;
            }
            skippedCount++;

            if (!cellEnumerator.MoveNext())
            {
                return HeaderRowResult.NoData;
            }
        }
        if (skippedCount != expectedSkippedCount)
        {
            return HeaderRowResult.WrongAmountSkipped(skippedCount);
        }
        while (true)
        {
            var text = cellEnumerator.Current.InnerText;
            var expectedId = goodCellCount + colOffset1;
            var group = c.Schedule.Group(text);
            Debug.Assert(expectedId == group.Id.Value);

            goodCellCount++;
            if (!cellEnumerator.MoveNext())
            {
                return HeaderRowResult.Good(goodCellCount);
            }
        }
    }
}

[DoesNotReturn]
void TimeSlotError()
{
    throw new NotSupportedException("The time slot must contain the time range second");
}

public sealed class DocParseContext
{
    public required ScheduleBuilder Schedule;
    public required LessonTimeConfig TimeConfig;
    public required DayNameParser DayNameParser;
    public required Maps Maps;

    public struct CreateParams
    {
        public required DayNameProvider DayNameProvider;
    }

    public static DocParseContext Create(CreateParams p)
    {
        var s = new ScheduleBuilder
        {
            ValidationSettings = new()
            {
                SubGroup = SubGroupValidationMode.PossiblyIncreaseSubGroupCount,
            },
        };
        var timeConfig = LessonTimeConfig.CreateDefault();

        return new()
        {
            Schedule = s,
            TimeConfig = timeConfig,
            DayNameParser = new DayNameParser(p.DayNameProvider),
            Maps = new(),
        };
    }

    public CourseId Course(string name) => Maps.Courses.GetOrAdd(name, x => Schedule.Course(x));
    public TeacherId Teacher(string name) => Maps.Teachers.GetOrAdd(name, x => Schedule.Teacher(x));
    public RoomId Room(string name) => Maps.Rooms.GetOrAdd(name, x => Schedule.Room(x));
}

// TODO: Read the whole table once to find these first.
public readonly struct DayNameParser(DayNameProvider p)
{
    private readonly Dictionary<string, DayOfWeek> _days = CreateMappings(p);

    public DayOfWeek? Map(string s)
    {
        if (_days.TryGetValue(s, out var day))
        {
            return day;
        }
        return null;
    }

    private static Dictionary<string, DayOfWeek> CreateMappings(DayNameProvider p)
    {
        var ret = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < p.Names.Length; index++)
        {
            string name = p.Names[index];
            ret.Add(name, (DayOfWeek) index);
        }
        return ret;
    }
}

public readonly struct NameMap<T>() where T : struct
{
    public readonly Dictionary<string, T> _values = new();

    public T GetOrAdd(string name, Func<string, T> add)
    {
        if (_values.TryGetValue(name, out var value))
        {
            return value;
        }

        value = add(name);
        _values.Add(name, value);
        return value;
    }
}

public readonly struct Maps()
{
    public readonly NameMap<CourseId> Courses = new();
    public readonly NameMap<TeacherId> Teachers = new();
    public readonly NameMap<RoomId> Rooms = new();
}

public readonly record struct ColumnCounts(int Skipped, int Good)
{
    public int Total => Skipped + Good;
}

public struct TimeParsingState()
{
    public int TimeSlotOrdinal;
    public TimeSlot TimeSlot;
}

public struct TableParsingState()
{
    public DayOfWeek? CurrentDay;
    public TimeParsingState? Time;
    public ColumnCounts? ColumnCounts;
}

public readonly struct HeaderRowResult
{
    public static HeaderRowResult Nothing => new(HeaderRowResultStatus.Nothing);
    public static HeaderRowResult WrongAmountSkipped(int skipped) => new(HeaderRowResultStatus.WrongAmountSkipped, skipped);
    public static HeaderRowResult NoData => new(HeaderRowResultStatus.NoData);
    public static HeaderRowResult Good(int goodCount) => new(HeaderRowResultStatus.GroupNames, goodCount);

    public readonly HeaderRowResultStatus Status;
    public readonly int Count;

    private HeaderRowResult(HeaderRowResultStatus status, int count = 0)
    {
        Status = status;
        Count = count;
    }

    public int GroupCount
    {
        get
        {
            if (Status != HeaderRowResultStatus.GroupNames)
            {
                throw new InvalidOperationException("The result is not good");
            }
            return Count;
        }
    }

    public int SkippedCount
    {
        get
        {
            if (Status != HeaderRowResultStatus.WrongAmountSkipped)
            {
                throw new InvalidOperationException("The result is not wrong amount skipped");
            }
            return Count;
        }
    }
}

public enum HeaderRowResultStatus
{
    Nothing,
    WrongAmountSkipped,
    NoData,
    GroupNames,
}

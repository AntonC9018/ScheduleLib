using System.Diagnostics;
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
foreach (var table in tables)
{
    var rows = table.ChildElements.OfType<TableRow>();
    int rowIndex = 0;
    using var rowEnumerator = rows.GetEnumerator();
    if (!rowEnumerator.MoveNext())
    {
        break;
    }

    IEnumerable<TableCell> Cells() => rowEnumerator.Current.ChildElements.OfType<TableCell>();

    int skippedCols = HeaderRow(colOffset);
    if (skippedCols != 2)
    {
        throw new NotSupportedException("Docs with only header 2 cols supported");
    }

    DayOfWeek currentDay = default;
    int currentTimeSlotOrdinal = 0;
    TimeSlot currentTimeSlot = default;

    const int dayColumnIndex = 0;
    const int timeSlotColumnIndex = 1;

    rowIndex++;
    while (rowEnumerator.MoveNext())
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
                    currentDay = DayOfWeekCol();
                    break;
                }
                case timeSlotColumnIndex:
                {
                    (currentTimeSlotOrdinal, currentTimeSlot) = TimeSlotCol();
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
                    if (colSpan != 0)
                    {
                        throw new NotSupportedException("The day and time slot columns must be one column in width");
                    }
                    break;
                }
            }
            columnIndex += colSpan;


            DayOfWeek DayOfWeekCol()
            {
                if (props?.VerticalMerge is not { } mergeStart)
                {
                    throw new NotSupportedException("Invalid format");
                }

                if (mergeStart.Val is not { } mergeStartVal
                    || mergeStartVal == MergedCellValues.Continue)
                {
                    return currentDay;
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

            (int Ordinal, TimeSlot TimeSlot) TimeSlotCol()
            {
                if (props?.VerticalMerge is not { } mergeStart)
                {
                    throw new NotSupportedException("Invalid format");
                }

                if (mergeStart.Val is not { } mergeStartVal
                    || mergeStartVal == MergedCellValues.Continue)
                {
                    return (currentTimeSlotOrdinal, currentTimeSlot);
                }

                if (mergeStart.Val != MergedCellValues.Restart)
                {
                    throw new NotSupportedException($"Unsupported merge cell command: {mergeStart.Val}");
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
                    if (num != currentTimeSlotOrdinal + 1)
                    {
                        throw new NotSupportedException("The time slot number must be in order");
                    }

                    newTimeSlotOrdinal = num;
                }

                if (!paragraphs.MoveNext())
                {
                    throw new NotSupportedException("");
                }

                var time = Time();

                if (paragraphs.MoveNext())
                {
                    throw new NotSupportedException("Extra paragraphs");
                }

                {
                    var timeStarts = c.TimeConfig.TimeSlotStarts;
                    var timeSlotIndex = currentTimeSlot.Index + 1;
                    if (timeStarts[timeSlotIndex] != time.Start)
                    {
                        throw new NotSupportedException("The time slots must follow each other");
                    }

                    var expectedEndTime = time.Start.Add(c.TimeConfig.LessonDuration);
                    if (expectedEndTime != time.End)
                    {
                        throw new NotSupportedException($"The lesson durations must all be equal to the default duration ({c.TimeConfig.LessonDuration} minutes)");
                    }

                    return (newTimeSlotOrdinal, new(timeSlotIndex));
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

            }

            void NormalCol(int colSpan)
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
                        int timeSlotIndex = Array.BinarySearch(c.TimeConfig.TimeSlotStarts, startTime);
                        if (timeSlotIndex == -1)
                        {
                            TimeSlotError();
                        }

                        l.TimeSlot(new(timeSlotIndex));
                    }

                    l.DayOfWeek(currentDay);

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
                        for (int i = 0; i < colSpan; i++)
                        {
                            var groupId = GroupId(i + columnIndex);
                            groups.Add(groupId);
                        }

                        if (groups.Count > 0
                            && lesson.SubGroupNumber != SubGroupNumber.All)
                        {
                            throw new NotSupportedException("Lessons with for subgroups with multiple groups not supported");
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
    }

    _ = rowIndex;

    GroupId GroupId(int colIndex)
    {
        return new(colOffset + colIndex);
    }

    int HeaderRow(int colOffset1)
    {
        int skippedCount = 0;
        int goodCellIndex = 0;
        using var cellEnumerator = Cells().GetEnumerator();
        if (cellEnumerator.MoveNext())
        {
            return 0;
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
                return skippedCount;
            }
        }
        while (true)
        {
            var text = cellEnumerator.Current.InnerText;
            var expectedId = goodCellIndex + colOffset1;
            var group = c.Schedule.Group(text);
            Debug.Assert(expectedId == group.Id.Value);

            goodCellIndex++;
            if (!cellEnumerator.MoveNext())
            {
                return skippedCount;
            }
        }
    }
}

void TimeSlotError()
{
    throw new NotSupportedException("The time slot must contain the time range second");
}

public sealed class DocParseContext()
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

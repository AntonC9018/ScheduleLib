using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing.Word;

public sealed class DocParseContext
{
    public required ScheduleBuilder Schedule;
    public required LessonTimeConfig TimeConfig;
    public required CourseNameParserConfig CourseNameParserConfig;
    public required DayNameParser DayNameParser;
    public required Maps Maps;

    public struct CreateParams
    {
        public required DayNameProvider DayNameProvider;
        public required CourseNameParserConfig CourseNameParserConfig;
    }

    public static DocParseContext Create(CreateParams p)
    {
        var s = new ScheduleBuilder
        {
            ValidationSettings = new()
            {
                SubGroup = SubGroupValidationMode.PossiblyRegisterSubGroup,
            },
        };
        var timeConfig = LessonTimeConfig.CreateDefault();

        return new()
        {
            Schedule = s,
            TimeConfig = timeConfig,
            CourseNameParserConfig = p.CourseNameParserConfig,
            DayNameParser = new DayNameParser(p.DayNameProvider),
            Maps = new(),
        };
    }

    public CourseId Course(string name)
    {
        ref var courseId = ref CollectionsMarshal.GetValueRefOrAddDefault(
            Maps.Courses,
            name,
            out bool exists);

        if (exists)
        {
            return courseId;
        }

        var parsedCourse = CourseNameParserConfig.Parse(name);
        // TODO: N^2, use some sort of hash to make this faster.
        foreach (var t in Maps.SlowCourses)
        {
            if (!parsedCourse.IsEqual(t.Name))
            {
                continue;
            }

            courseId = t.CourseId;
            return courseId;
        }

        var result = Schedule.Courses.New();
        courseId = new(result.Id);

        Maps.SlowCourses.Add(new(parsedCourse, courseId));

        return courseId;
    }

    public TeacherId Teacher(string name) => Maps.Teachers.GetOrAdd(name, x => Schedule.Teacher(x));
    public RoomId Room(string name) => Maps.Rooms.GetOrAdd(name, x => Schedule.Room(x));

    public Schedule BuildSchedule()
    {
        var courseNamesByKey = Maps.Courses
            .GroupBy(x => x.Value)
            .Select(x =>
            {
                var names = x.Select(x1 => x1.Key).ToArray();
                Array.Sort(names, static (a, b) => b.Length - a.Length);
                return (x.Key, Names: names);
            });

        foreach (var t in courseNamesByKey)
        {
            ref var b = ref Schedule.Courses.Ref(t.Key.Id);
            b.Names = t.Names;
            Console.WriteLine(string.Join("; ", t.Names));
        }

        var ret = Schedule.Build();
        return ret;
    }
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
        var ret = new Dictionary<string, DayOfWeek>(StringComparer.CurrentCultureIgnoreCase);
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
    public readonly Dictionary<string, T> Values = new();

    public T GetOrAdd(string name, Func<string, T> add)
    {
        if (Values.TryGetValue(name, out var value))
        {
            return value;
        }

        value = add(name);
        Values.Add(name, value);
        return value;
    }
}

public readonly record struct SlowCourse(
    ParsedCourseName Name,
    CourseId CourseId);

public readonly struct Maps()
{
    public readonly Dictionary<string, CourseId> Courses = new();
    public readonly List<SlowCourse> SlowCourses = new();

    public readonly NameMap<TeacherId> Teachers = new();
    public readonly NameMap<RoomId> Rooms = new();
    public readonly Dictionary<string, GroupId> Groups = new();
}


file readonly record struct ColumnCounts(int Skipped, int Good)
{
    public int Total => Skipped + Good;
}

file struct TimeParsingState()
{
    public int TimeSlotOrdinal;
    public TimeSlot TimeSlot;
}

file struct TableParsingState()
{
    public DayOfWeek? CurrentDay;
    public TimeParsingState? Time;
    public ColumnCounts? ColumnCounts;
    public int GroupsProcessed = 0;

    public GroupId GroupId(int colIndex)
    {
        return new(GroupsProcessed + colIndex - ColumnCounts!.Value.Skipped);
    }
}

public struct ParseWordParams
{
    public required DocParseContext Context { get; init; }
    public required WordprocessingDocument Document { get; init; }
}

public static class WordScheduleParser
{
    public static void ParseToSchedule(ParseWordParams p)
    {
        var doc = p.Document;
        var c = p.Context;

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

            if (MaybeParseHeaderRow())
            {
                // Table with rows other than the header is allowed ig.
                if (!rowEnumerator.MoveNext())
                {
                    continue;
                }
            }

            const int dayColumnIndex = 0;
            const int timeSlotColumnIndex = 1;

            while (true)
            {
                ParseRegularRow();

                if (!rowEnumerator.MoveNext())
                {
                    break;
                }
            }
            continue;

            void ParseRegularRow()
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
                                timeText = "";
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
                            else
                            {
                                l.TimeSlot(state.Time!.Value.TimeSlot);
                            }

                            l.DayOfWeek(state.CurrentDay!.Value);

                            {
                                var courseName = lesson.LessonName.ToString();
                                var courseId = c.Course(courseName);
                                l.Course(courseId);
                            }
                            foreach (var t in lesson.TeacherNames)
                            {
                                var teacherName = t.ToString();
                                var teacherId = c.Teacher(teacherName);
                                l.Teacher(teacherId);
                            }
                            if (!lesson.RoomName.IsEmpty)
                            {
                                var roomName = lesson.RoomName.ToString();
                                var roomId = c.Room(roomName);
                                l.Room(roomId);
                            }
                            l.Type(lesson.LessonType);
                            l.Parity(lesson.Parity);

                            ref var g = ref l.Model.Group;
                            if (lesson.GroupName.IsEmpty)
                            {
                                var groups = new LessonGroups();
                                for (int i = 0; i < colSpan1; i++)
                                {
                                    var groupId = state.GroupId(i + columnIndex);
                                    groups.Add(groupId);
                                }

                                // if (groups.Count > 1
                                //     && lesson.SubGroupNumber != SubGroupNumber.All)
                                // {
                                //     throw new NotSupportedException("Lessons with subgroups with multiple groups not supported");
                                // }

                                g.Groups = groups;
                            }
                            else
                            {
                                // validate group
                                var groupName = lesson.GroupName.Span.Trim().ToString();
                                if (!c.Maps.Groups.TryGetValue(groupName, out var x))
                                {
                                    throw new NotSupportedException("If a group is mentioned in lesson modifiers, it should have been declared prior");
                                }
                                g.Groups.Add(x);
                            }

                            g.SubGroup = lesson.SubGroup;
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

            int FindTimeSlotIndex(TimeOnly start)
            {
                int i = Array.BinarySearch(c.TimeConfig.TimeSlotStarts, start);
                if (i == -1)
                {
                    TimeSlotError();
                }
                return i;
            }

            // Returns whether the header row is present
            bool MaybeParseHeaderRow()
            {
                const int expectedSkippedCount = 2;

                using var cellEnumerator = Cells().GetEnumerator();
                int skippedCount = SkipEmpties();
                if (!ShouldParseGroupsWithChecks(skippedCount))
                {
                    return false;
                }
                {
                    if (state.ColumnCounts is { } cc)
                    {
                        state.GroupsProcessed += cc.Good;
                    }
                }
                int groupCount = AddGroups();
                state.ColumnCounts = new(skippedCount, groupCount);
                return true;

                int SkipEmpties()
                {
                    int skippedCount1 = 0;
                    if (!cellEnumerator.MoveNext())
                    {
                        throw new NotSupportedException("Empty table not supported");
                    }
                    while (true)
                    {
                        var text = cellEnumerator.Current.InnerText;
                        if (text != "")
                        {
                            break;
                        }
                        skippedCount1++;

                        if (!cellEnumerator.MoveNext())
                        {
                            throw new NotSupportedException("Header columns expected after the empties");
                        }
                    }
                    return skippedCount1;
                }

                bool ShouldParseGroupsWithChecks(int skippedCount1)
                {
                    if (skippedCount1 == expectedSkippedCount)
                    {
                        return true;
                    }

                    if (skippedCount1 != 0)
                    {
                        throw new NotSupportedException($"Expected {expectedSkippedCount} empty columns for the left header");
                    }

                    if (state.ColumnCounts is null)
                    {
                        throw new NotSupportedException("Parsing must begin with a table that has the header with the groups");
                    }

                    return false;
                }

                int AddGroups()
                {
                    int goodCellCount = 0;
                    while (true)
                    {
                        var groupName = cellEnumerator.Current.InnerText;
                        var expectedId = goodCellCount + state.GroupsProcessed;

                        var group = c.Schedule.Group(groupName);
                        Debug.Assert(expectedId == group.Id.Value);

                        ref var groupIdRef = ref CollectionsMarshal.GetValueRefOrAddDefault(
                            c.Maps.Groups,
                            group.Ref.Name,
                            out bool groupAddedPreviously);
                        if (groupAddedPreviously)
                        {
                            throw new NotSupportedException("Each group must only be used in a column header once.");
                        }
                        groupIdRef = group.Id;

                        goodCellCount++;
                        if (!cellEnumerator.MoveNext())
                        {
                            return goodCellCount;
                        }
                    }
                }
            }
        }
    }

    [DoesNotReturn]
    private static void TimeSlotError()
    {
        throw new NotSupportedException("The time slot must contain the time range second");
    }
}

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing.WordDoc;

public sealed class DocParseContext
{
    public required ScheduleBuilder Schedule { get; init; }
    public required LessonTimeConfig TimeConfig { get; init; }
    public required DayNameParser DayNameParser { get; init; }
    public required CourseNameUnifierModule CourseNameUnifierModule { get; init; }

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
        s.EnableLookupModule();
        var timeConfig = LessonTimeConfig.CreateDefault();

        return new()
        {
            Schedule = s,
            TimeConfig = timeConfig,
            CourseNameUnifierModule = new(p.CourseNameParserConfig),
            DayNameParser = new DayNameParser(p.DayNameProvider),
        };
    }

    public Schedule BuildSchedule()
    {
        var courseNamesByKey = Schedule.LookupModule!.Courses
            .GroupBy(x => x.Value)
            .Select(x =>
            {
                var names = x.Select(x1 => x1.Key).ToArray();
                Array.Sort(names, static (a, b) => b.Length - a.Length);
                return (x.Key, Names: names);
            });

        foreach (var t in courseNamesByKey)
        {
            ref var b = ref Schedule.Courses.Ref(t.Key);
            b.Names = t.Names;
        }

        var ret = Schedule.Build();
        return ret;
    }

    internal CourseId Course(string name)
    {
        var ret = CourseNameUnifierModule.FindOrAdd(new()
        {
            Schedule = Schedule,
            CourseName = name,
            ParseOptions = new(),
        });
        return ret;
    }

    internal TeacherId Teacher(TeacherName name)
    {
        var nameModel = new TeacherBuilderModel.NameModel
        {
            ShortFirstName = name.ShortFirstName.IsEmpty
                ? null
                : new(name.ShortFirstName.ToString()),
            FirstName = null,
            LastName = name.LastName.ToString(),
        };

        // Need to remap explicitly, because we do the check for diacritics later.
        nameModel.LastName = Schedule.RemapTeacherName(nameModel.LastName);

        var teacherBuilder = Schedule.Teacher(nameModel);
        var teacher = teacherBuilder.Model;

        if (!teacher.Name.LastName!.Equals(
            nameModel.FirstName,
            StringComparison.CurrentCultureIgnoreCase))
        {
            teacher.Name.LastName = nameModel.LastName;
        }

        return teacherBuilder.Id;
    }

    internal RoomId Room(string name) => Schedule.Room(name);
}

// TODO: Read the whole table once to find these first.
public sealed class DayNameParser(DayNameProvider p)
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

internal readonly record struct SlowCourse(
    ParsedCourseName Name,
    CourseId CourseId);

internal readonly record struct ColumnCounts(int Skipped, int Good)
{
    public int Total => Skipped + Good;
}

internal struct TimeParsingState()
{
    public int TimeSlotOrdinal;
    public TimeSlot TimeSlot;
}

internal struct TableParsingState()
{
    public DayOfWeek? CurrentDay;
    public TimeParsingState? Time;
    public ColumnCounts? ColumnCounts;
    public required int GroupsProcessed;

    public readonly GroupId GroupId(int colIndex)
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
        var state = new TableParsingState
        {
            GroupsProcessed = p.Context.Schedule.Groups.Count,
        };

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
                const int dayColumnIndex = 0;
                const int timeSlotColumnIndex = 1;

                int columnIndex = 0;
                int columnSizeCounter = 0;
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

                    int countLeft = state.ColumnCounts!.Value.Total - columnSizeCounter;
                    if (countLeft < colSpan)
                    {
                        throw new NotSupportedException("The column count is off");
                    }

                    switch (columnIndex)
                    {
                        case dayColumnIndex:
                        case timeSlotColumnIndex:
                        {
                            columnIndex += 1;
                            break;
                        }
                        default:
                        {
                            columnIndex += colSpan;
                            break;
                        }
                    }
                    columnSizeCounter += colSpan;
                    continue;


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

                            int ret = c.FindTimeSlotIndex(parsedTime.Start);
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
                            c.AddOrMergeLesson(
                                in state,
                                in lesson,
                                columnIndex: columnIndex,
                                colSpan: colSpan1);
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

            // Returns whether the header row is present
            bool MaybeParseHeaderRow()
            {
                const int expectedSkippedCount = 2;

                using var cellEnumerator = Cells().GetEnumerator();
                (int skippedCount, int skippedSize) = SkipEmpties();
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
                state.ColumnCounts = new(skippedSize, groupCount);
                return true;

                (int SkippedCount, int SkippedSize) SkipEmpties()
                {
                    int skippedCount1 = 0;
                    int skippedSize1 = 0;
                    if (!cellEnumerator.MoveNext())
                    {
                        throw new NotSupportedException("Empty table not supported");
                    }
                    while (true)
                    {
                        var cell = cellEnumerator.Current;
                        var text = cell.InnerText;
                        if (text != "")
                        {
                            break;
                        }
                        skippedCount1++;

                        var size = cell.TableCellProperties?.GridSpan?.Val ?? 1;
                        skippedSize1 += size;

                        if (!cellEnumerator.MoveNext())
                        {
                            throw new NotSupportedException("Header columns expected after the empties");
                        }
                    }
                    return (skippedCount1, skippedSize1);
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
                    int goodSize = 0;
                    while (true)
                    {
                        var cell = cellEnumerator.Current;
                        var groupName = cell.InnerText;
                        var expectedId = goodSize + state.GroupsProcessed;

                        var group = c.Schedule.Group(groupName);
                        if (expectedId != group.Id.Value)
                        {
                            throw new NotSupportedException("Each group must only be used in a column header once.");
                        }
                        Debug.Assert(expectedId == group.Id.Value);

                        var colSpan = cell.TableCellProperties?.GridSpan?.Val ?? 1;
                        goodSize += colSpan;

                        if (!cellEnumerator.MoveNext())
                        {
                            return goodSize;
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

    private static void AddOrMergeLesson(
        this DocParseContext c,
        in TableParsingState state,
        in ParsedLesson lesson,
        int columnIndex,
        int colSpan)
    {
        RegularLessonBuilderModelData modelData = new();

        if (lesson.StartTime is { } startTime)
        {
            int timeSlotIndex = c.FindTimeSlotIndex(startTime);
            modelData.Date.TimeSlot = new(timeSlotIndex);
        }
        else
        {
            modelData.Date.TimeSlot = state.Time!.Value.TimeSlot;
        }

        modelData.Date.DayOfWeek = state.CurrentDay!.Value;

        CourseId courseId;
        {
            var courseName = lesson.LessonName.ToString();
            courseId = c.Course(courseName);
            modelData.General.Course = courseId;
        }
        foreach (var t in lesson.TeacherNames)
        {
            var teacherId = c.Teacher(t);
            modelData.General.Teachers.Add(teacherId);
        }
        if (!lesson.RoomName.IsEmpty)
        {
            var roomName = lesson.RoomName.ToString();
            var roomId = c.Room(roomName);
            modelData.General.Room = roomId;
        }

        modelData.General.Type = lesson.LessonType;
        modelData.Date.Parity = lesson.Parity;

        ref var g = ref modelData.Group;
        if (lesson.GroupName.IsEmpty)
        {
            var groups = new LessonGroups();
            for (int i = 0; i < colSpan; i++)
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
            if (!c.Schedule.LookupModule!.Groups.TryGetValue(groupName, out var x))
            {
                throw new NotSupportedException("If a group is mentioned in lesson modifiers, it should have been declared prior");
            }
            g.Groups.Add(new(x));
        }

        g.SubGroup = lesson.SubGroup;

        if (MaybeMergeIntoAnExistingLesson())
        {
            return;
        }

        _ = c.Schedule.RegularLesson(modelData);
        return;

        bool MaybeMergeIntoAnExistingLesson()
        {
            var schedule = c.Schedule;
            var lessonsByCourse = schedule.LookupModule!.LessonsByCourse;
            var existingLessonsOfThisCourse = lessonsByCourse[courseId];

            foreach (var existingLesson in existingLessonsOfThisCourse)
            {
                var model = schedule.RegularLessons.Ref(existingLesson);

                var diffMask = new RegularLessonModelDiffMask
                {
                    Parity = true,
                    Day = true,
                    TimeSlot = true,
                    SubGroup = true,
                    Room = true,
                    LessonType = true,
                    // Already checked because we look up by it.
                    // Course = true,
                };
                var diff = LessonBuilderHelper.Diff(
                    modelData,
                    model.Data,
                    diffMask);
                if (diff.TheyDiffer)
                {
                    continue;
                }

                LessonBuilderHelper.Merge(
                    to: ref model.Data,
                    from: modelData,
                    new()
                    {
                        Groups = true,
                        Teachers = true,
                    });
                return true;
            }
            return false;
        }
    }

    private static int FindTimeSlotIndex(this DocParseContext c, TimeOnly start)
    {
        int i = Array.BinarySearch(c.TimeConfig.TimeSlotStarts, start);
        if (i == -1)
        {
            TimeSlotError();
        }
        return i;
    }
}

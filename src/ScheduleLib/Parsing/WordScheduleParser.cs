using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
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
            ParseOptions = new()
            {
                // Commas are allowed in course names now, apparently.
                IgnorePunctuation = true,
            },
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

            var headerParseResult = MaybeParseHeaderRow(c, ref state, rowEnumerator);
            switch (headerParseResult.Status)
            {
                case HeaderRowParseStatus.HeaderParsed:
                {
                    // Table with no rows other than the header is allowed ig.
                    if (!rowEnumerator.MoveNext())
                    {
                        continue;
                    }
                    break;
                }
                case HeaderRowParseStatus.NoHeaderParsed:
                {
                    break;
                }
                // The master doc has a table used for alignment
                // with general info before the main table.
                // We ignore it.
                case HeaderRowParseStatus.SkipTable:
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
                foreach (var cell in rowEnumerator.Current.Cells())
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

                        var lines = Lines();
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
                        return;

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

                        IEnumerable<string> Lines()
                        {
                            var copy = cell.CloneNode(deep: true);
                            RemoveHyperlinks(copy);

                            foreach (var para in cell.ChildElements.OfType<Paragraph>())
                            {
                                yield return para.InnerText;
                            }
                            yield break;

                            void RemoveHyperlinks(OpenXmlElement el)
                            {
                                el.RemoveAllChildren<Hyperlink>();
                                foreach (var child in el.ChildElements)
                                {
                                    RemoveHyperlinks(child);
                                }
                            }
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
            var groupFullName = lesson.GroupName.Span.Trim().ToString();
            if (c.Schedule.Lookup().Group(groupFullName) is not { } groupId)
            {
                throw new NotSupportedException("If a group is mentioned in lesson modifiers, it should have been declared prior");
            }
            g.Groups.Add(groupId);
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

    private static IEnumerable<TableCell> Cells(this TableRow row)
    {
        return row.ChildElements.OfType<TableCell>();
    }

    private static HeaderRowParseResult MaybeParseHeaderRow(
        DocParseContext c,
        ref TableParsingState state,
        IEnumerator<TableRow> rowEnumerator)
    {
        const int expectedEmptySkippedCount = 2;

        IEnumerator<TableCell>? cellEnumerator = null;
        try
        {
            cellEnumerator = rowEnumerator.Current.Cells().GetEnumerator();
            if (!cellEnumerator.MoveNext())
            {
                throw new NotSupportedException("Empty table not supported");
            }

            // empties    group names
            var skippedInfo = TryFirstFormat();
            if (skippedInfo.IsNotMatch)
            {
                // sem    date range, year
                // ---  group names
                skippedInfo = TrySecondFormat();
            }

            if (skippedInfo.IsNotMatch)
            {
                if (state.ColumnCounts is null)
                {
                    return new(HeaderRowParseStatus.SkipTable);
                }
                return new(HeaderRowParseStatus.NoHeaderParsed);
            }

            // Currently the enumerator is at the group names (already primed with MoveNext).
            {
                if (state.ColumnCounts is { } cc)
                {
                    state.GroupsProcessed += cc.Good;
                }

                int groupCount = AddGroups(state.GroupsProcessed);
                state.ColumnCounts = new(skippedInfo.Size, groupCount);
                return new(HeaderRowParseStatus.HeaderParsed);
            }
        }
        finally
        {
            cellEnumerator?.Dispose();
        }

        SkippedHeaderColumnsInfo TryFirstFormat()
        {
            var skippedInfo = TrySkipEmpties();
            if (skippedInfo.Count == 0)
            {
                return skippedInfo;
            }
            if (skippedInfo.Count != expectedEmptySkippedCount)
            {
                throw new NotSupportedException($"Expected {expectedEmptySkippedCount} empty header columns");
            }
            return skippedInfo;
        }

        void MoveToNextCellForGroupRow()
        {
            if (!cellEnumerator.MoveNext())
            {
                throw new NotSupportedException("Header columns expected after the left header");
            }
        }

        SkippedHeaderColumnsInfo TrySkipEmpties()
        {
            int skippedCount1 = 0;
            int skippedSize1 = 0;
            while (true)
            {
                var cell = cellEnumerator.Current;
                // TODO: Try HasChildren
                var text = cell.InnerText;
                if (text != "")
                {
                    break;
                }
                skippedCount1++;

                int size = cell.GetWidth();
                skippedSize1 += size;
                MoveToNextCellForGroupRow();
            }
            return new(skippedCount1, skippedSize1);
        }

        SkippedHeaderColumnsInfo TrySecondFormat()
        {
            var skippedWidth = TrySecondFormatUpperLeftMostHeader();
            if (skippedWidth == 0)
            {
                return SkippedHeaderColumnsInfo.NotMatch();
            }

            // Now make sure we skip the same size on the next row.
            rowEnumerator.MoveNext();
            cellEnumerator.Dispose();
            cellEnumerator = rowEnumerator.Current.Cells().GetEnumerator();

            if (!cellEnumerator.MoveNext())
            {
                throw new NotSupportedException("Expected the second row");
            }

            var width = cellEnumerator.Current.GetWidth();
            if (width != skippedWidth)
            {
                throw new NotSupportedException("The second row must have the same width as the first row");
            }

            MoveToNextCellForGroupRow();

            return new(1, skippedWidth);
        }

        // Returns the size of the column.
        int TrySecondFormatUpperLeftMostHeader()
        {
            var cell = cellEnumerator.Current;
            using var paragraphs = cell
                .ChildElements
                .OfType<Paragraph>()
                .GetEnumerator();
            if (!paragraphs.MoveNext())
            {
                return 0;
            }
            if (ParseSem() is not { } semNumber)
            {
                return 0;
            }
            if (!paragraphs.MoveNext())
            {
                throw new NotSupportedException("Expected the interval");
            }

            var interval = ParseInterval();

            // Ignored for now.
            _ = semNumber;
            _ = interval;

            // Could make sure the next one is the year?
            // The rest of this row is ignored.
            return cell.GetWidth();

            int? ParseSem()
            {
                var semPara = paragraphs.Current.InnerText;
                var parser = new Parser(semPara);
                parser.SkipWhitespace();
                if (!parser.ConsumeExactString("Sem."))
                {
                    return null;
                }

                parser.SkipWhitespace();
                {
                    var bparser = parser.BufferedView();
                    {
                        var result = bparser.SkipNotWhitespace();
                        if (!result.EndOfInput)
                        {
                            throw new NotSupportedException("Sem must be followed by a roman number");
                        }
                    }
                    {
                        var numberSpan = parser.PeekSpanUntilPosition(bparser.Position);
                        var number = NumberHelper.FromRoman(numberSpan);
                        if (number is not { } n)
                        {
                            throw new NotSupportedException("Sem must be followed by a roman number");
                        }
                        return n;
                    }
                }
            }

            (DateTime Start, DateTime End) ParseInterval()
            {
                var parser = new Parser(paragraphs.Current.InnerText);
                parser.SkipWhitespace();
                var bparser = parser.BufferedView();
                var skipped = bparser.SkipUntil(['–', '-', '—']);
                if (skipped.EndOfInput)
                {
                    throw new NotSupportedException("Expected interval separator");
                }

                var startSpan = parser.PeekSpanUntilPosition(bparser.Position);
                var startDate = ParseDateTime(startSpan, "Invalid start date");

                bparser.Move();
                parser.MoveTo(bparser.Position);

                var endSpan = parser.PeekSpanUntilEnd();
                var endDate = ParseDateTime(endSpan, "Invalid end date");

                if (startDate >= endDate)
                {
                    throw new NotSupportedException("The start date must be before the end date");
                }

                return (startDate, endDate);

                DateTime ParseDateTime(ReadOnlySpan<char> s, string error)
                {
                    const string format = "dd.MM.yyyy";
                    if (!DateTime.TryParseExact(
                            s: s,
                            format: format,
                            provider: null,
                            style: default,
                            result: out var date))
                    {
                        throw new NotSupportedException(error);
                    }
                    return date;
                }
            }
        }

        int AddGroups(int groupsProcessed)
        {
            int goodSize = 0;
            while (true)
            {
                var cell = cellEnumerator.Current;
                var groupName = cell.InnerText;
                var expectedId = goodSize + groupsProcessed;

                var group = c.Schedule.Group(groupName);
                if (expectedId != group.Id.Value)
                {
                    throw new NotSupportedException("Each group must only be used in a column header once.");
                }
                Debug.Assert(expectedId == group.Id.Value);

                var colSpan = cell.GetWidth();
                goodSize += colSpan;

                if (!cellEnumerator.MoveNext())
                {
                    return goodSize;
                }
            }
        }
    }

    private enum HeaderRowParseStatus
    {
        HeaderParsed,
        NoHeaderParsed,
        SkipTable,
    }
    private readonly record struct HeaderRowParseResult(HeaderRowParseStatus Status);

    private readonly record struct SkippedHeaderColumnsInfo(int Count, int Size)
    {
        public static SkippedHeaderColumnsInfo NotMatch() => default;
        public bool IsNotMatch => Count == 0;
    }

    private static int GetWidth(this TableCell cell)
    {
        return cell.TableCellProperties?.GridSpan?.Val ?? 1;
    }
}

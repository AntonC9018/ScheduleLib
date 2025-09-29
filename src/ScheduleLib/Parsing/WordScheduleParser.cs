using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing.WordDoc;

public sealed class DocParseContext
{
    public required ScheduleBuilder Schedule { get; init; }
    public required LessonTimeConfig TimeConfig { get; init; }
    public required DayNameParser DayNameParser { get; init; }
    public required CourseNameUnifierModule CourseNameUnifierModule { get; init; }
    internal PeriodId CurrentPeriodId { get; private set; } = PeriodId.Unspecified;

    public void SetPeriod(PeriodBeginning? period)
    {
        if (period is { } per)
        {
            CurrentPeriodId = Period(per.StartDate);
        }
        else
        {
            CurrentPeriodId = PeriodId.Unspecified;
        }
    }


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
            FirstName = name.FirstName.Map(x =>
            {
                var ret = default(OptionalNamePart);
                if (x.IsEmpty)
                {
                    return ret;
                }
                var w = new WordSpan(x.Span);
                if (w.LooksFull)
                {
                    ret.Full = x.ToString();
                    return ret;
                }
                else
                {
                    ret.Short = x.ToString();
                    return ret;
                }
            }),
            LastName = new(name.LastName.Map(x =>
            {
                if (x.IsEmpty)
                {
                    return null;
                }
                return x.ToString();
            })),
        };

        // Need to remap explicitly, because we do the check for diacritics later.
        nameModel.LastName = Schedule.RemapTeacherName(nameModel.LastName);

        var teacherBuilder = Schedule.Teacher(nameModel);
        var teacher = teacherBuilder.Model;

        bool savedTeacherNameHasDiacritics = teacher.Name.LastName.Parts.EachEquals(
            nameModel.LastName.Parts,
            (a, b) =>
            {
                if (a is null)
                {
                    return b is null;
                }
                return a.Equals(b, StringComparison.CurrentCultureIgnoreCase);
            });
        if (!savedTeacherNameHasDiacritics)
        {
            teacher.Name.LastName = nameModel.LastName;
        }

        return teacherBuilder.Id;
    }

    internal RoomId Room(string name) => Schedule.Room(name);

    private PeriodId Period(DateOnly start)
    {
        // Currently, assume that periods are going to be ordered.
        var periods = Schedule.Periods.List;
        Debug.Assert(periods.IsSorted(x => x.Start));

        if (periods.Count == 0)
        {
            return CreatePeriod();
        }

        var lastPeriod = periods[^1];
        if (lastPeriod.Start == start)
        {
            OutOfOrderCheck(periods.SkipLast(1), start);
            return new(periods.Count - 1);
        }

        OutOfOrderCheck(periods, start);

        // Maybe want to encapsulate this more, use the builder?
        Debug.Assert(lastPeriod.EndExclusive == default);
        lastPeriod.EndExclusive = start;
        return CreatePeriod();

        [Conditional("DEBUG")]
        static void OutOfOrderCheck(IEnumerable<PeriodBuilderModel> periods, DateOnly start)
        {
            Debug.Assert(periods.All(x => x.Start < start), "Out of order periods not implemented");
        }

        PeriodId CreatePeriod()
        {
            var ret = Schedule.Period(start);
            return ret;
        }
    }
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

internal readonly record struct ColumnCounts(SkippedHeaderColumnsInfo Skipped, int Good)
{
    // public int SkippedCount => Skipped.Count;
    public int SkippedSize => Skipped.Size;
    public int Total => Skipped.Size + Good;
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
    public SizedItemArray<GroupId> CurrentGroups = new();
    public int? Format = null;

    public readonly GroupId GroupId(int colIndex)
    {
        int i = colIndex - ColumnCounts!.Value.SkippedSize;
        return CurrentGroups.Find(i);
    }
}

public struct PeriodBeginning
{
    public required DateOnly StartDate { get; init; }
}

public struct ParseWordParams
{
    public required DocParseContext Context { get; init; }
    public required WordprocessingDocument Document { get; init; }
}

public static class WordScheduleParser
{
    private sealed class CopyableRowEnumerator : IEnumerator<TableRow>
    {
        private List<TableRow>.Enumerator _enumerator;

        public CopyableRowEnumerator(List<TableRow>.Enumerator enumerator)
        {
            _enumerator = enumerator;
        }

        public List<TableRow>.Enumerator Copy() => _enumerator;

        public void Dispose() => _enumerator.Dispose();
        public bool MoveNext() => _enumerator.MoveNext();
        public TableRow Current => _enumerator.Current;
        object IEnumerator.Current => Current;
        public void Reset() => throw new NotSupportedException();
    }

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
            .OfType<Table>();
        var state = new TableParsingState
        {
        };

        foreach (var table in tables)
        {
            var rows = table.ChildElements.OfType<TableRow>().ToList();
            // NOTE: has to be copyable, but also pass-by-reference.
            using var rowEnumerator = new CopyableRowEnumerator(rows.GetEnumerator());
            if (!rowEnumerator.MoveNext())
            {
                break;
            }

            var headerParseResult = MaybeParseHeaderRow(c, ref state, rowEnumerator);
            switch (headerParseResult.Status)
            {
                case HeaderRowParseStatus.HeaderParsed:
                {
                    state.Format = headerParseResult.Format;

                    // Table with no rows other than the header is allowed ig.
                    if (!rowEnumerator.MoveNext())
                    {
                        continue;
                    }
                    break;
                }
                case HeaderRowParseStatus.NoHeaderParsed:
                {
                    // if (state.Format != 0)
                    // {
                    //     throw new NotImplementedException("Format other than 1 not supported this");
                    // }
                    if (!UpdateSizesByInspectingFutureCells())
                    {
                        throw new NotImplementedException("A more intelligent way to figure out the widths");
                    }
                    break;

                    bool UpdateSizesByInspectingFutureCells()
                    {
                        var rowEnumeratorCopy = rowEnumerator.Copy();
                        var newGroupsArr = new SizedItemArray<GroupId>();
                        while (true)
                        {
                            int skippedSize = 0;

                            foreach (var cell in IterateCellColumns(rowEnumeratorCopy.Current))
                            {
                                switch (cell.ColumnType)
                                {
                                    case ColumnType.Regular:
                                    {
                                        var size = cell.ColSpan;
                                        int pos = cell.ColumnSizeCounter - skippedSize;
                                        // Add an invalid id, just to set up the breakpoints
                                        var result = newGroupsArr.ReplaceAt(
                                            pos,
                                            new(GroupId.Invalid, size),
                                            allowAddToEnd: true);
                                        Debug.Assert(result is ReplaceItemStatus.Spliced
                                            or ReplaceItemStatus.FullyReplaced
                                            or ReplaceItemStatus.PartlyReplaced
                                            or ReplaceItemStatus.AddedAtEnd
                                            or ReplaceItemStatus.ExistingItemTooSmall);
                                        break;
                                    }
                                    case ColumnType.TimeSlot:
                                    case ColumnType.DayOfWeek:
                                    {
                                        skippedSize += cell.ColSpan;
                                        break;
                                    }
                                }
                            }

                            if (state.CurrentGroups.Count != newGroupsArr.Count)
                            {
                                if (!rowEnumeratorCopy.MoveNext())
                                {
                                    return false;
                                }
                                continue;
                            }

                            {
                                var group = state.CurrentGroups.GetEnumerator();
                                var newGroup = newGroupsArr.EnumerateWithPosition().GetEnumerator();
                                while (true)
                                {
                                    bool g = group.MoveNext();
                                    bool ng = newGroup.MoveNext();
                                    Debug.Assert(g == ng);
                                    if (!g)
                                    {
                                        break;
                                    }

                                    newGroupsArr.ReplaceItem(
                                        newGroup.Current.Position,
                                        group.Current.Item);
                                }
                            }

                            state.ColumnCounts = new(
                                Skipped: new(Count: 2, Size: skippedSize),
                                Good: newGroupsArr.TotalSize);
                            state.CurrentGroups = newGroupsArr;
                            return true;
                        }
                    }
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

            static IEnumerable<ColumnCell> IterateCellColumns(TableRow row)
            {
                const int dayColumnIndex = 0;
                const int timeSlotColumnIndex = 1;

                int columnIndex = 0;
                int columnSizeCounter = 0;
                foreach (var cell in row.Cells())
                {
                    var props = cell.TableCellProperties;
                    var colSpan = props?.GridSpan?.Val ?? 1;

                    ColumnType colType;
                    switch (columnIndex)
                    {
                        case dayColumnIndex:
                        {
                            colType = ColumnType.DayOfWeek;
                            break;
                        }
                        case timeSlotColumnIndex:
                        {
                            colType = ColumnType.TimeSlot;
                            break;
                        }
                        default:
                        {
                            colType = ColumnType.Regular;
                            break;
                        }
                    }

                    yield return new ColumnCell(
                        ColumnType: colType,
                        ColumnSizeCounter: columnSizeCounter,
                        ColSpan: colSpan,
                        Cell: cell);

                    switch (colType)
                    {
                        case ColumnType.TimeSlot:
                        case ColumnType.DayOfWeek:
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
                }
            }

            void ParseRegularRow()
            {
                foreach (var cell in IterateCellColumns(rowEnumerator.Current))
                {
                    switch (cell.ColumnType)
                    {
                        case ColumnType.DayOfWeek:
                        {
                            var newDay = DayOfWeekCol(state.CurrentDay);
                            if (newDay != state.CurrentDay)
                            {
                                state.CurrentDay = newDay;
                                state.Time = null;
                            }
                            break;
                        }
                        case ColumnType.TimeSlot:
                        {
                            state.Time = TimeSlotCol(state.Time);
                            break;
                        }
                        case ColumnType.Regular:
                        {
                            NormalCol(cell.ColSpan);
                            break;
                        }
                        default:
                        {
                            throw Unreachable();
                        }
                    }

                    int countLeft = state.ColumnCounts!.Value.Total - cell.ColumnSizeCounter;
                    if (countLeft < cell.ColSpan)
                    {
                        throw new NotSupportedException("The column count is off");
                    }

                    continue;

                    DayOfWeek DayOfWeekCol(DayOfWeek? currentDay)
                    {
                        if (cell.Cell.TableCellProperties?.VerticalMerge is not { } mergeStart)
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

                        if (cell.Cell.InnerText is not { } dayNameText)
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
                            if (cell.Cell.TableCellProperties?.VerticalMerge is { } mergeStart)
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

                        // May be two paragraphs, may be one
                        {
                            using var paragraphs = cell.Cell.ChildElements.OfType<Paragraph>().GetEnumerator();
                            if (!paragraphs.MoveNext())
                            {
                                throw new NotSupportedException("Invalid time slot cell");
                            }
                        }

                        var timeSlotCellText = cell.Cell.InnerText;
                        var parser = new Parser(timeSlotCellText);

                        int newTimeSlotOrdinal;
                        {
                            var maybeNum = parser.ReadRoman();
                            if (maybeNum.Status != ReadRomanStatus.Ok)
                            {
                                throw new NotSupportedException("The time slot number should be a roman numeral");
                            }
                            int currentOrdinal = currentTime?.TimeSlotOrdinal ?? 0;
                            int num = maybeNum.Number;
                            if (num != currentOrdinal + 1)
                            {
                                throw new NotSupportedException("The time slot number must be in order");
                            }

                            newTimeSlotOrdinal = num;
                        }

                        if (parser.SkipWhitespace().EndOfInput)
                        {
                            throw new InvalidOperationException("Expected time after the time slot");
                        }

                        var parsedTime = Time(ref parser);

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

                        static (TimeOnly Start, TimeOnly End) Time(ref Parser parser)
                        {
                            // HH:MM-HH:MM
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
                                columnIndex: cell.ColumnSizeCounter,
                                colSpan: colSpan1,
                                periodId: p.Context.CurrentPeriodId);
                        }
                        return;

                        bool ShouldAdd()
                        {
                            if (cell.Cell.TableCellProperties?.VerticalMerge is not { } merge)
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
                            var copy = cell.Cell.CloneNode(deep: true);
                            RemoveHyperlinks(copy);

                            foreach (var para in copy.ChildElements.OfType<Paragraph>())
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
        int colSpan,
        PeriodId periodId)
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
        var groupFullName = lesson.GroupName.Span.Trim().ToString();
        if (groupFullName.Length == 0
            || HandleSpecialSubGroup(ref g, lesson))
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
            var groupId = c.Schedule.Group(groupFullName);
            g.Groups.Add(groupId);
        }

        // Check for special case when it's a subgroup.
        bool HandleSpecialSubGroup(
            ref RegularLessonBuilderModelData.GroupData g,
            in ParsedLesson lesson)
        {
            var specialGroups = new[]
            {
                "începători",
                "ro",
                "ru",
                "eng",
                "opțional",
            };
            foreach (var group in specialGroups)
            {
                if (!IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(group, groupFullName))
                {
                    continue;
                }
                if (lesson.SubGroup.Value is not null)
                {
                    throw new NotImplementedException("Multiple subgroups as a single group");
                }
                g.SubGroup = new(group);
                return true;
            }
            return false;
        }

        modelData.General.Period = periodId;

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
                var model = schedule.RegularLessons.Ref(existingLesson.Id);

                var diffMask = new RegularLessonModelDiffMask
                {
                    Parity = true,
                    Day = true,
                    TimeSlot = true,
                    SubGroup = true,
                    Room = true,
                    LessonType = true,
                    Period = true,
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
        var timeSlot = c.TimeConfig.FindTimeSlotByStartTime(start);
        if (timeSlot is not { } v)
        {
            TimeSlotError();
            throw null!;
        }
        return v.Index;
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
            int format = 0;
            if (skippedInfo.IsNotMatch)
            {
                // sem    date range, year
                // ---  group names
                skippedInfo = TrySecondFormat();
                format = 1;
            }

            if (skippedInfo.IsNotMatch)
            {
                if (state.ColumnCounts is null)
                {
                    return new(HeaderRowParseStatus.SkipTable, format);
                }
                return new(HeaderRowParseStatus.NoHeaderParsed, format);
            }

            // Currently the enumerator is at the group names (already primed with MoveNext).
            {
                state.CurrentGroups.Clear();
                int groupCount = AddGroups(state.CurrentGroups);
                state.ColumnCounts = new(skippedInfo, groupCount);
                return new(HeaderRowParseStatus.HeaderParsed, format);
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
                var res = parser.ReadRoman();
                if (res.Status != ReadRomanStatus.Ok)
                {
                    throw new InvalidOperationException("Sem must be followed by a roman numeral");
                }

                if (!parser.IsEmpty)
                {
                    throw new InvalidOperationException("Roman numeral after sem must be the last thing");
                }

                return res.Number;
            }

            (DateTime Start, DateTime End) ParseInterval()
            {
                var parser = new Parser(paragraphs.Current.InnerText);
                parser.SkipWhitespace();
                var bparser = parser.BufferedView();
                var skipped = bparser.SkipUntilAny(['–', '-', '—']);
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
                    var culture = CultureInfo.CurrentCulture;
                    Debug.Assert(culture.Calendar.TwoDigitYearMax == 2049,
                        "Fix this if you want to parse older docs");

                    const string format = "dd.MM.yy";
                    if (!DateTime.TryParseExact(
                            s: s,
                            format: format,
                            provider: culture,
                            style: default,
                            result: out var date))
                    {
                        throw new NotSupportedException(error);
                    }
                    return date;
                }
            }
        }

        int AddGroups(SizedItemArray<GroupId> outputGroups)
        {
            while (true)
            {
                var cell = cellEnumerator.Current;
                var groupName = cell.InnerText;
                var group = c.Schedule.Group(groupName);
                var colSpan = cell.GetWidth();
                outputGroups.Add(new(group, colSpan));

                if (!cellEnumerator.MoveNext())
                {
                    return outputGroups.TotalSize;
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
    private readonly record struct HeaderRowParseResult(HeaderRowParseStatus Status, int Format);
}

public static class WordprocessingHelper
{
    public static int GetWidth(this TableCell cell)
    {
        return cell.TableCellProperties?.GridSpan?.Val ?? 1;
    }

    public static IEnumerable<(TableCell Cell, int Position)> WithPosition(this IEnumerable<TableCell> e)
    {
        var pos = 0;
        foreach (var x in e)
        {
            yield return (x, pos);
            pos += x.GetWidth();
        }
    }
}

internal readonly record struct SkippedHeaderColumnsInfo(int Count, int Size)
{
    public static SkippedHeaderColumnsInfo NotMatch() => default;
    public bool IsNotMatch => Count == 0;
}


internal enum ColumnType
{
    DayOfWeek,
    TimeSlot,
    Regular,
}
internal readonly record struct ColumnCell(
    ColumnType ColumnType,
    int ColumnSizeCounter,
    int ColSpan,
    TableCell Cell);

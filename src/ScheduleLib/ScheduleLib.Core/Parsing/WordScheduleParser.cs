using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing.WordDoc;

public enum SubGroupStatus
{
    GroupNameIsSubGroup,
    SetFromSubGroup,
}

public sealed class DocParseContext
{
    public ScheduleBuilder Schedule { get; }
    public LessonTimeConfig TimeConfig { get; }
    public DayNameParser DayNameParser { get; }
    public CourseNameUnifierModule CourseNameUnifierModule { get; }
    public LessonParserFactory ParserFactory { get; }
    public PeriodId CurrentPeriodId { get; private set; } = PeriodId.Unspecified;

    public DocParseContext(
        CourseNameUnifierModule courseNameUnifierModule,
        DayNameParser dayNameParser,
        LessonParserFactory parserFactory,
        ScheduleBuilder schedule,
        LessonTimeConfig timeConfig)
    {
        CourseNameUnifierModule = courseNameUnifierModule;
        DayNameParser = dayNameParser;
        ParserFactory = parserFactory;
        Schedule = schedule;
        TimeConfig = timeConfig;
    }


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
        public required CourseNameUnifierConfig CourseNameUnifierConfig;
        public required LessonParserFactory ParserFactory;
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

        return new(
            schedule: s,
            timeConfig: timeConfig,
            courseNameUnifierModule: new(p.CourseNameUnifierConfig),
            dayNameParser: new DayNameParser(p.DayNameProvider),
            parserFactory: p.ParserFactory);
    }

    public SubGroupStatus SetCommonProps(
        ILessonBuilder<ILessonBuilderModel> builder,
        in ParsedLesson parsedLesson)
    {
        var courseId = GetOrAddCourse(parsedLesson.LessonName);
        builder.Course(courseId);

        if (!parsedLesson.RoomName.IsEmpty)
        {
            var roomId = GetOrAddRoom(parsedLesson.RoomName.ToString());
            builder.Room(roomId);
        }

        foreach (var tname in parsedLesson.TeacherNames)
        {
            var teacherId = GetOrAddTeacher(tname);
            builder.Teacher(teacherId);
        }

        builder.Type(parsedLesson.LessonType);
        builder.Period(CurrentPeriodId);

        if (HandleSpecialSubGroup(parsedLesson, builder))
        {
            if (parsedLesson.SubGroup != SubGroup.All)
            {
                throw new InvalidOperationException("SubGroup specified twice?");
            }
            return SubGroupStatus.GroupNameIsSubGroup;
        }
        else
        {
            builder.SubGroup(parsedLesson.SubGroup);
            return SubGroupStatus.SetFromSubGroup;
        }

        // Check for special case when it's a subgroup.
        static bool HandleSpecialSubGroup(
            in ParsedLesson lesson,
            ILessonBuilder<ILessonBuilderModel> builder)
        {
            if (lesson.GroupName.IsEmpty)
            {
                return false;
            }
            var specialGroups = SpecialSubGroups.AllSpecial;
            foreach (var group in specialGroups)
            {
                if (!IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(group.Value!, lesson.GroupName.Span))
                {
                    continue;
                }
                if (lesson.SubGroup.Value is not null)
                {
                    throw new NotImplementedException("Multiple subgroups as a single group");
                }
                builder.SubGroup(group);
                return true;
            }
            return false;
        }

    }

    public CourseId GetOrAddCourse(ReadOnlyMemory<char> name)
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

    public TeacherId GetOrAddTeacher(TeacherName name)
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
        _ = Schedule.RemapTeacherName(ref nameModel);

        var teacherBuilder = Schedule.Teacher(nameModel);
        var teacher = teacherBuilder.Model;

        var before = teacher.Name.LastName;
        _ = before;

        teacher.Name.LastName.Parts.Update(
            nameModel.LastName.Parts, (a, b) =>
            {
                if (a == null || b == null)
                {
                    return a ?? b;
                }
                var ret = DiacriticsHelper.SelectWithDiacritics(a, b);
                return ret;
            });

        return teacherBuilder.Id;
    }

    public RoomId GetOrAddRoom(string name) => Schedule.Room(name);

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

    public DayOfWeek? Map(ReadOnlySpan<char> s)
    {
        if (_days.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(s, out var day))
        {
            return day;
        }
        return null;
    }

    private static Dictionary<string, DayOfWeek> CreateMappings(DayNameProvider p)
    {
        var ret = new Dictionary<string, DayOfWeek>(IgnoreDiacriticsAndCaseComparer.Instance);
        for (int index = 0; index < p.Names.Length; index++)
        {
            string name = p.Names[index];
            ret.Add(name, (DayOfWeek) index);

            if (RomanianLanguageHelper.VariantWithOldLetter(name) is { } x)
            {
                ret.Add(x, (DayOfWeek) index);
            }
        }
        return ret;
    }
}

public static class RomanianLanguageHelper
{
    // Romanian special case: â -> î in the middle of the word.
    // This is surprisingly common even though it's wrong.
    public static string? VariantWithOldLetter(string name)
    {
        var middle = name.AsSpan()[1 .. ^1];
        if (!middle.Contains('â'))
        {
            return null;
        }

        return string.Create(name.Length, middle, (output, middle) =>
        {
            Debug.Assert(name.Length <= 256);
            {
                middle.Replace(output[1 .. ^1], 'â', 'î');
            }
            output[0] = name[0];
            output[^1] = name[^1];
        });
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

                        var parsedTime = parser.ParseTimeInterval();

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

                        var l = Lines();
                        try
                        {
                            // ReSharper disable once PossibleMultipleEnumeration
                            using var lines = l.GetEnumerator();

                            // TODO: can reuse this.
                            var lessonParser = p.Context.ParserFactory.Create();
                            lessonParser.Lexer.Reset(lines);
                            var lessons = lessonParser.ParseLessons(new StringBuilder());

                            foreach (var lesson in lessons)
                            {
                                c.AddOrMergeLesson(
                                    in state,
                                    in lesson,
                                    columnIndex: cell.ColumnSizeCounter,
                                    colSpan: colSpan1);
                            }
                        }
                        catch (WrongFormatException e)
                        {
                            // ReSharper disable once PossibleMultipleEnumeration
                            throw new NotSupportedException($"Unsupported syntax when parsing {string.Join('\n', l)}", e);
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

                        IEnumerable<ReadOnlyMemory<char>> Lines()
                        {
                            var copy = cell.Cell.CloneNode(deep: true);
                            RemoveHyperlinks(copy);

                            foreach (var para in copy.ChildElements.OfType<Paragraph>())
                            {
                                yield return para.InnerText.AsMemory();
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
        // TODO: This is scuffed.
        var builder = c.Schedule.DetachedRegularLesson();

        if (lesson.StartTime is { } startTime)
        {
            int timeSlotIndex = c.FindTimeSlotIndex(startTime);
            builder.TimeSlot(new(timeSlotIndex));
        }
        else
        {
            builder.TimeSlot(state.Time!.Value.TimeSlot);
        }

        builder.DayOfWeek(state.CurrentDay!.Value);
        builder.Parity(lesson.Parity);

        bool groupNameHandledAsSubgroup = c.SetCommonProps(builder, lesson) == SubGroupStatus.GroupNameIsSubGroup;
        if (lesson.GroupName.IsEmpty || groupNameHandledAsSubgroup)
        {
            var groups = new LessonGroups();
            for (int i = 0; i < colSpan; i++)
            {
                var groupId = state.GroupId(i + columnIndex);
                groups.Add(groupId);
            }
            builder.Groups([.. groups]);
        }
        else if (!groupNameHandledAsSubgroup)
        {
            var g = lesson.GroupName.ToString();
            var groupId = c.Schedule.Group(g);
            builder.Group(groupId);
        }

        if (MaybeMergeIntoAnExistingLesson())
        {
            return;
        }

        builder.Attach();
        return;

        bool MaybeMergeIntoAnExistingLesson()
        {
            var schedule = c.Schedule;
            var lessonsByCourse = schedule.LookupModule!.LessonsByCourse;
            var courseId = builder.Model.General.Course!.Value;
            var existingLessonsOfThisCourse = lessonsByCourse[courseId];

            foreach (var existingLesson in existingLessonsOfThisCourse)
            {
                var model = schedule.WeeklyLessons.Ref(existingLesson.Id);

                var diffMask = new LessonModelDiffMask
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
                    builder.Model.Data,
                    model.Data,
                    diffMask);
                if (diff.TheyDiffer)
                {
                    continue;
                }

                LessonBuilderHelper.Merge(
                    to: ref model.Data,
                    from: builder.Model.Data,
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

            {
                var parser = new Parser(paragraphs.Current.InnerText);
                var interval = parser.ParseDateInterval("dd.MM.yy");

                // Ignored for now.
                _ = semNumber;
                _ = interval;
            }

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

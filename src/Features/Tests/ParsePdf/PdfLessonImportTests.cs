using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Import.Pdf;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

public sealed class PdfLessonImportTests
{
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "Gr.1")]
    [InlineData(true, "Gr.1")]
    public void DefaultAndCallbackPathsPreserveLessonCoordinatesAndAlternative(bool useCallback, string? alternative)
    {
        var context = CreateContext();
        var cell = new PdfScheduleCell(Source: "test.pdf",
            1,
            ["IA2601(ro)", "IA2602(ro)"],
            Day: "Marți",
            Start: "8:00",
            Text: "15:00 HTML (lab, imp)\nG.Marin 326/4",
            Alternative: alternative);
        var groups = cell.Groups.Select(g => context.Schedule.Group(g).Id)
            .ToArray();
        var calls = 0;

        global::ScheduleLib.Parsing.ScheduleImportContext context2 = context;
        global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Import.Pdf.PdfScheduleCell> cells = [cell];
        global::ScheduleLib.Import.Pdf.ImportedLessonHandler? addLesson;
        if (useCallback)
        {
            addLesson = Add;
        }
        else
        {
            addLesson = null;
        }

        PdfScheduleImporter.Import(context2,
            cells,
            addLesson);

        int expected5;
        if (useCallback)
        {
            expected5 = 1;
        }
        else
        {
            expected5 = 0;
        }

        int actual4 = calls;
        Assert.Equal(expected: expected5, actual: actual4);
        var lesson = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(expected: groups, actual: lesson.Base.Group.Groups.ToArray());
        Assert.Equal(expected: DayOfWeek.Tuesday, actual: lesson.Date.DayOfWeek);
        Assert.Equal(expected: 4, actual: lesson.Date.TimeSlot!.Value.Index);
        Assert.Equal(expected: Parity.OddWeek, actual: lesson.Date.Parity);
        global::ScheduleLib.Alternative expected2 = new(alternative);
        global::ScheduleLib.Alternative actual2 = lesson.Base.Group.Alternative;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: context.CurrentPeriodId, actual: lesson.Base.General.Period);
        global::ScheduleLib.CourseId expected3 = context.GetOrAddCourse("HTML".AsMemory());
        global::ScheduleLib.CourseId? expected4 = expected3;
        global::ScheduleLib.CourseId? actual3 = lesson.Base.General.Course;
        Assert.Equal(expected: expected4, actual: actual3);

        void Add(ImportedLessonInput input)
        {
            calls++;
            Assert.Equal(expected: "HTML", actual: input.Lesson.LessonName.ToString());
            global::System.TimeOnly expected2 = new TimeOnly(hour: 15, minute: 0);
            global::System.TimeOnly? expected3 = expected2;
            global::System.TimeOnly? actual2 = input.Lesson.StartTime;
            Assert.Equal(expected: expected3, actual: actual2);
            Assert.Equal(expected: DayOfWeek.Tuesday, actual: input.Day);
            Assert.Equal(expected: TimeSlot.First, actual: input.TimeSlot);
            Assert.Equal(expected: groups, actual: input.Groups);
            global::ScheduleLib.Alternative expected4 = new(alternative);
            global::ScheduleLib.Alternative actual3 = input.Alternative;
            Assert.Equal(expected: expected4, actual: actual3);
            context.AddOrMergeLesson(input);
        }
    }

    [Fact]
    public void CallbackCanConsumeLessonWithoutAddingIt()
    {
        var context = CreateContext();
        var calls = 0;

        PdfScheduleImporter.Import(context,
            [Cell()],
            _ =>
            {
                calls++;
            });

        Assert.Equal(expected: 1, actual: calls);
        Assert.Empty(context.Schedule.WeeklyLessons.List);
    }

    [Fact]
    public void CallbackFormatErrorKeepsCellContextAndContinuesWithFollowingCells()
    {
        var context = CreateContext();
        string[] originalGroups = ["IA2601(ro)"];
        var first = Cell() with
        {
            Groups = originalGroups,
        };
        var next = first with
        {
            Page = 2,
        };
        var failure = new FormatException("Callback failure");
        var calls = 0;

        var error = Assert.Throws<AggregateException>(() => PdfScheduleImporter.Import(context,
            [first, next],
            Add));

        Assert.Equal(expected: 2, actual: calls);
        global::System.Exception objectValue = Assert.Single(error.InnerExceptions);
        object objectValue2 = objectValue;
        var cellError = Assert.IsType<PdfScheduleCellImportException>(objectValue2);
        originalGroups[0] = "changed after failure";
        Assert.Equal(expected: "test.pdf", actual: cellError.SourceName);
        Assert.Equal(expected: 1, actual: cellError.Page);
        Assert.Equal(expected: ["IA2601(ro)"], actual: cellError.Groups);
        Assert.Same(expected: failure, actual: cellError.InnerException);
        Assert.Contains(expectedSubstring: "test.pdf, page 1, Luni 8:00", actualString: cellError.Message);
        Assert.Single(context.Schedule.WeeklyLessons.List);

        void Add(ImportedLessonInput input)
        {
            calls++;
            if (calls == 1)
            {
                throw failure;
            }
            context.AddOrMergeLesson(input);
        }
    }

    [Fact]
    public void UnexpectedCallbackErrorPropagatesUnwrapped()
    {
        var context = CreateContext();
        var failure = new InvalidOperationException("Unexpected callback failure");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PdfScheduleImporter.Import(context,
                [Cell()],
                _ => throw failure));

        Assert.Same(expected: failure, actual: error);
    }

    [Fact]
    public void InvalidTimeOverrideKeepsDomainErrorInsideCellContext()
    {
        var context = CreateContext();
        var cell = Cell() with
        {
            Text = "7:13 HTML (lab)",
        };

        var error = Assert.Throws<AggregateException>(() => PdfScheduleImporter.Import(context, [cell]));

        global::System.Exception objectValue = Assert.Single(error.InnerExceptions);
        object objectValue2 = objectValue;
        var cellError = Assert.IsType<PdfScheduleCellImportException>(objectValue2);
        Assert.IsType<InvalidScheduleDocumentException.UnknownTimeSlotException>(cellError.InnerException);
        Assert.Empty(context.Schedule.WeeklyLessons.List);
    }

    private static PdfScheduleCell Cell()
    {
        return new(Source: "test.pdf",
            1,
            ["IA2601(ro)"],
            Day: "Luni",
            Start: "8:00",
            Text: "HTML (lab)");
    }

    private static ScheduleImportContext CreateContext()
    {
        var context = ScheduleImportContext.Create(new()
        {
            DayNameProvider = new(),
            CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new()),
            SpecializationRegistry = Config.SpecializationRegistry,
        });
        context.Schedule.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        context.SetPeriod(new()
        {
            StartDate = new(year: 2026,
                month: 9,
                day: 14),
        });
        return context;
    }
}

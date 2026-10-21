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
        var cell = new PdfScheduleCell("test.pdf",
            1,
            ["IA2601(ro)", "IA2602(ro)"],
            "Marți",
            "8:00",
            "15:00 HTML (lab, imp)\nG.Marin 326/4",
            alternative);
        var groups = cell.Groups.Select(g => context.Schedule.Group(g).Id).ToArray();
        var calls = 0;

        PdfScheduleImporter.Import(context,
            [cell],
            useCallback ? Add : null);

        Assert.Equal(useCallback ? 1 : 0, calls);
        var lesson = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(groups, lesson.Base.Group.Groups.ToArray());
        Assert.Equal(DayOfWeek.Tuesday, lesson.Date.DayOfWeek);
        Assert.Equal(4, lesson.Date.TimeSlot!.Value.Index);
        Assert.Equal(Parity.OddWeek, lesson.Date.Parity);
        Assert.Equal(new(alternative), lesson.Base.Group.Alternative);
        Assert.Equal(context.CurrentPeriodId, lesson.Base.General.Period);
        Assert.Equal(context.GetOrAddCourse("HTML".AsMemory()), lesson.Base.General.Course);

        void Add(ImportedLessonInput input)
        {
            calls++;
            Assert.Equal("HTML", input.Lesson.LessonName.ToString());
            Assert.Equal(new TimeOnly(15, 0), input.Lesson.StartTime);
            Assert.Equal(DayOfWeek.Tuesday, input.Day);
            Assert.Equal(TimeSlot.First, input.TimeSlot);
            Assert.Equal(groups, input.Groups);
            Assert.Equal(new(alternative), input.Alternative);
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
            _ => calls++);

        Assert.Equal(1, calls);
        Assert.Empty(context.Schedule.WeeklyLessons.List);
    }

    [Fact]
    public void CallbackFormatErrorKeepsCellContextAndContinuesWithFollowingCells()
    {
        var context = CreateContext();
        var first = Cell();
        var next = first with { Page = 2 };
        var failure = new FormatException("Callback failure");
        var calls = 0;

        var error = Assert.Throws<AggregateException>(() => PdfScheduleImporter.Import(context,
            [first, next],
            Add));

        Assert.Equal(2, calls);
        var cellError = Assert.IsType<FormatException>(Assert.Single(error.InnerExceptions));
        Assert.Same(failure, cellError.InnerException);
        Assert.Contains("test.pdf, page 1, Luni 8:00", cellError.Message);
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

        Assert.Same(failure, error);
    }

    [Fact]
    public void InvalidTimeOverrideKeepsDomainErrorInsideCellContext()
    {
        var context = CreateContext();
        var cell = Cell() with { Text = "7:13 HTML (lab)" };

        var error = Assert.Throws<AggregateException>(() => PdfScheduleImporter.Import(context, [cell]));

        var cellError = Assert.IsType<FormatException>(Assert.Single(error.InnerExceptions));
        Assert.IsType<InvalidScheduleDocumentException>(cellError.InnerException);
        Assert.Empty(context.Schedule.WeeklyLessons.List);
    }

    private static PdfScheduleCell Cell() => new("test.pdf",
        1,
        ["IA2601(ro)"],
        "Luni",
        "8:00",
        "HTML (lab)");

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
            StartDate = new(2026,
                9,
                14),
        });
        return context;
    }
}

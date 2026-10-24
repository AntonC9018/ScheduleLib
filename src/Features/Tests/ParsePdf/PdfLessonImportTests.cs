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
        var groups = cell.Groups.Select(g => context.Schedule.Group(g).Id).ToArray();
        var calls = 0;

        PdfScheduleImporter.Import(context,
            [cell],
            useCallback ? Add : null);

        Assert.Equal(expected: useCallback ? 1 : 0, actual: calls);
        var lesson = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(expected: groups, actual: lesson.Base.Group.Groups.ToArray());
        Assert.Equal(expected: DayOfWeek.Tuesday, actual: lesson.Date.DayOfWeek);
        Assert.Equal(expected: 4, actual: lesson.Date.TimeSlot!.Value.Index);
        Assert.Equal(expected: Parity.OddWeek, actual: lesson.Date.Parity);
        Assert.Equal(expected: new(alternative), actual: lesson.Base.Group.Alternative);
        Assert.Equal(expected: context.CurrentPeriodId, actual: lesson.Base.General.Period);
        Assert.Equal(expected: context.GetOrAddCourse("HTML".AsMemory()), actual: lesson.Base.General.Course);

        void Add(ImportedLessonInput input)
        {
            calls++;
            Assert.Equal(expected: "HTML", actual: input.Lesson.LessonName.ToString());
            Assert.Equal(expected: new TimeOnly(hour: 15, minute: 0), actual: input.Lesson.StartTime);
            Assert.Equal(expected: DayOfWeek.Tuesday, actual: input.Day);
            Assert.Equal(expected: TimeSlot.First, actual: input.TimeSlot);
            Assert.Equal(expected: groups, actual: input.Groups);
            Assert.Equal(expected: new(alternative), actual: input.Alternative);
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

        Assert.Equal(expected: 1, actual: calls);
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

        Assert.Equal(expected: 2, actual: calls);
        var cellError = Assert.IsType<FormatException>(Assert.Single(error.InnerExceptions));
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
        var cell = Cell() with { Text = "7:13 HTML (lab)" };

        var error = Assert.Throws<AggregateException>(() => PdfScheduleImporter.Import(context, [cell]));

        var cellError = Assert.IsType<FormatException>(Assert.Single(error.InnerExceptions));
        Assert.IsType<InvalidScheduleDocumentException>(cellError.InnerException);
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

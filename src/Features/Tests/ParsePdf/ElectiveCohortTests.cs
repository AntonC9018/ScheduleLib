using FmiScheduleImport;
using ScheduleLib.Import.Pdf;
using ScheduleLib.Parsing;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

public sealed class ElectiveCohortTests
{
    [Fact]
    public void SharedLecturesAppearInBothCohortsWithoutChangingNumericSubgroups()
    {
        var context = CreateContext();
        var groups = new[]
        {
            "I2502(ru)",
            "IA2503(ru)",
            "IA2504(ru)",
            "IA2505(ru)",
        };
        var cohorts = new ElectiveCohorts(context,
        [
            new("test.pdf",
                1,
                [groups[0], groups[2]],
                "Luni",
                "8:00",
                "",
                "Antreprenoriat Gr.1"),
            new("test.pdf",
                1,
                [groups[1], groups[2]],
                "Luni",
                "8:00",
                "",
                "Antreprenoriat Gr.2"),
        ]);
        var ids = groups.Select(g => context.Schedule.Group(g).Id).ToArray();
        var lesson = new ParsedLesson
        {
            LessonName = "Antreprenoriat inovativ".AsMemory(),
            TeacherNames = [],
            RoomName = default,
            GroupName = default,
            StartTime = new(15, 0),
            Parity = Parity.OddWeek,
        };
        var input = new ImportedLessonInput(lesson,
            DayOfWeek.Tuesday,
            TimeSlot.First,
            ids);
        cohorts.Add(input);
        Assert.Same(ids, input.Groups);
        Assert.Equal(default, input.Alternative);
        var lectures = context.Schedule.WeeklyLessons.List;
        Assert.Equal(3, lectures.Count);
        Assert.All(lectures,
            l =>
            {
                Assert.Equal(DayOfWeek.Tuesday, l.Date.DayOfWeek);
                Assert.Equal(4, l.Date.TimeSlot!.Value.Index);
                Assert.Equal(Parity.OddWeek, l.Date.Parity);
            });
        var fallback = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat inovativ"));
        Assert.Equal(ids[3], Assert.Single(fallback.Base.Group.Groups));
        Assert.All(lectures, l => Assert.Equal(SubGroup.All, l.Base.Group.SubGroup));
        var first = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat Gr.1"));
        Assert.Equal(new[]
            {
                ids[0],
                ids[2],
            },
            first.Base.Group.Groups.ToArray());
        var second = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat Gr.2"));
        Assert.Equal(new[]
            {
                ids[1],
                ids[2],
            },
            second.Base.Group.Groups.ToArray());
        Assert.Contains(Config.ImplicitSplitConfig.Scopes,
            s => s.AlternativeCourses.ContainsKey("Antreprenoriat inovativ"));
    }

    [Fact]
    public void ExplicitCohortFlowsThroughPdfCallbackWithoutExpansion()
    {
        var context = CreateContext();
        var cell = new PdfScheduleCell("test.pdf",
            1,
            ["IA2504(ru)"],
            "Luni",
            "8:00",
            "Antreprenoriat inovativ (curs)",
            "Antreprenoriat Gr.1");
        var cohorts = new ElectiveCohorts(context,
            [cell, cell with { Alternative = "Antreprenoriat Gr.2" }]);

        PdfScheduleImporter.Import(context,
            [cell],
            cohorts.Add);

        var lesson = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(new("Antreprenoriat Gr.1"), lesson.Base.Group.Alternative);
        Assert.Equal(context.GetOrAddCourse("Antreprenoriat inovativ".AsMemory()), lesson.Base.General.Course);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Antreprenoriat Gr.1")]
    public void OtherCoursesRequireNoCohortAnnotation(string? alternative)
    {
        var context = CreateContext();
        var cell = new PdfScheduleCell("test.pdf",
            1,
            ["IA2504(ru)"],
            "Luni",
            "8:00",
            "HTML (lab)",
            alternative);
        var cohorts = new ElectiveCohorts(context, [cell]);

        if (alternative is not null)
        {
            var error = Assert.Throws<AggregateException>(() => PdfScheduleImporter.Import(context,
                [cell],
                cohorts.Add));
            var cellError = Assert.IsType<FormatException>(Assert.Single(error.InnerExceptions));
            Assert.Equal("Cohort annotation on an unexpected course.", cellError.InnerException!.Message);
            Assert.Empty(context.Schedule.WeeklyLessons.List);
            return;
        }

        PdfScheduleImporter.Import(context,
            [cell],
            cohorts.Add);

        var lesson = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(default, lesson.Base.Group.Alternative);
        Assert.Equal(context.GetOrAddCourse("HTML".AsMemory()), lesson.Base.General.Course);
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
        return context;
    }
}

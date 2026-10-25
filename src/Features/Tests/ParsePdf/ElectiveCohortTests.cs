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
            new(Source: "test.pdf",
                1,
                [groups[0], groups[2]],
                Day: "Luni",
                Start: "8:00",
                Text: "",
                Alternative: "Antreprenoriat Gr.1"),
            new(Source: "test.pdf",
                1,
                [groups[1], groups[2]],
                Day: "Luni",
                Start: "8:00",
                Text: "",
                Alternative: "Antreprenoriat Gr.2"),
        ]);
        var ids = groups.Select(g => context.Schedule.Group(g).Id)
            .ToArray();
        var lesson = new ParsedLesson
        {
            LessonName = "Antreprenoriat inovativ".AsMemory(),
            TeacherNames = [],
            RoomName = default,
            GroupName = default,
            StartTime = new(hour: 15, minute: 0),
            Parity = Parity.OddWeek,
        };
        var input = new ImportedLessonInput(lesson,
            DayOfWeek.Tuesday,
            TimeSlot.First,
            ids);
        cohorts.Add(input);
        Assert.Same(expected: ids, actual: input.Groups);
        Assert.Equal(expected: default, actual: input.Alternative);
        var lectures = context.Schedule.WeeklyLessons.List;
        Assert.Equal(expected: 3, actual: lectures.Count);
        Assert.All(lectures,
            l =>
            {
                Assert.Equal(expected: DayOfWeek.Tuesday, actual: l.Date.DayOfWeek);
                Assert.Equal(expected: 4, actual: l.Date.TimeSlot!.Value.Index);
                Assert.Equal(expected: Parity.OddWeek, actual: l.Date.Parity);
            });
        var fallback = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat inovativ"));
        global::ScheduleLib.GroupId expected2 = ids[3];
        global::ScheduleLib.GroupId actual2 = Assert.Single(fallback.Base.Group.Groups);
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.All(lectures, l => Assert.Equal(expected: SubGroup.All, actual: l.Base.Group.SubGroup));
        var first = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat Gr.1"));
        Assert.Equal(expected: new[]
            {
                ids[0],
                ids[2],
            },
            actual: first.Base.Group.Groups.ToArray());
        var second = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat Gr.2"));
        Assert.Equal(expected: new[]
            {
                ids[1],
                ids[2],
            },
            actual: second.Base.Group.Groups.ToArray());
        Assert.Contains(Config.ImplicitSplitConfig.Scopes,
            s => s.AlternativeCourses.ContainsKey("Antreprenoriat inovativ"));
    }

    [Fact]
    public void ExplicitCohortFlowsThroughPdfCallbackWithoutExpansion()
    {
        var context = CreateContext();
        var cell = new PdfScheduleCell(Source: "test.pdf",
            1,
            ["IA2504(ru)"],
            Day: "Luni",
            Start: "8:00",
            Text: "Antreprenoriat inovativ (curs)",
            Alternative: "Antreprenoriat Gr.1");
        var cohorts = new ElectiveCohorts(context,
            [cell, cell with
            {
            Alternative = "Antreprenoriat Gr.2",
            }]);

        PdfScheduleImporter.Import(context,
            [cell],
            cohorts.Add);

        var lesson = Assert.Single(context.Schedule.WeeklyLessons.List);
        global::ScheduleLib.Alternative expected2 = new("Antreprenoriat Gr.1");
        global::ScheduleLib.Alternative actual2 = lesson.Base.Group.Alternative;
        Assert.Equal(expected: expected2, actual: actual2);
        global::ScheduleLib.CourseId expected3 = context.GetOrAddCourse("Antreprenoriat inovativ".AsMemory());
        global::ScheduleLib.CourseId? expected4 = expected3;
        global::ScheduleLib.CourseId? actual3 = lesson.Base.General.Course;
        Assert.Equal(expected: expected4, actual: actual3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Antreprenoriat Gr.1")]
    public void OtherCoursesRequireNoCohortAnnotation(string? alternative)
    {
        var context = CreateContext();
        var cell = new PdfScheduleCell(Source: "test.pdf",
            1,
            ["IA2504(ru)"],
            Day: "Luni",
            Start: "8:00",
            Text: "HTML (lab)",
            Alternative: alternative);
        var cohorts = new ElectiveCohorts(context, [cell]);

        if (alternative is not null)
        {
            var error = Assert.Throws<AggregateException>(() => PdfScheduleImporter.Import(context,
                [cell],
                cohorts.Add));
            global::System.Exception objectValue = Assert.Single(error.InnerExceptions);
            object objectValue2 = objectValue;
            var cellError = Assert.IsType<FormatException>(objectValue2);
            Assert.Equal(expected: "Cohort annotation on an unexpected course.", actual: cellError.InnerException!.Message);
            Assert.Empty(context.Schedule.WeeklyLessons.List);
            return;
        }

        PdfScheduleImporter.Import(context,
            [cell],
            cohorts.Add);

        var lesson = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(expected: default, actual: lesson.Base.Group.Alternative);
        global::ScheduleLib.CourseId expected2 = context.GetOrAddCourse("HTML".AsMemory());
        global::ScheduleLib.CourseId? expected3 = expected2;
        global::ScheduleLib.CourseId? actual2 = lesson.Base.General.Course;
        Assert.Equal(expected: expected3, actual: actual2);
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

using ScheduleLib.Parsing;
using System.Text;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class ScheduleCellParserTests
{
    [Fact]
    public void RepeatedCellsMergeGroupsAndKeepTimeOverrideAndPeriod()
    {
        var context = CreateContext();
        var first = context.Schedule.Group("IA2601(ro)").Id;
        var second = context.Schedule.Group("IA2602(ro)").Id;
        var lesson = Parse(context, "15:00 HTML (lab, imp)\nG.Marin 326/4");
        context.AddOrMergeLesson(new(lesson,
            DayOfWeek.Monday,
            TimeSlot.First,
            [first]));
        context.AddOrMergeLesson(new(lesson,
            DayOfWeek.Monday,
            TimeSlot.First,
            [second]));

        var result = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(expected: 2, actual: result.Base.Group.Groups.Count);
        Assert.Equal(expected: 4, actual: result.Date.TimeSlot!.Value.Index);
        Assert.Equal(expected: Parity.OddWeek, actual: result.Date.Parity);
        Assert.Equal(expected: default, actual: result.Base.Group.Alternative);
        Assert.Equal(expected: context.CurrentPeriodId, actual: result.Base.General.Period);
    }

    [Fact]
    public void DifferentCohortAlternativesDoNotMergeSharedLectures()
    {
        var context = CreateContext();
        var group = context.Schedule.Group("IA2504(ru)").Id;
        var lesson = Parse(context, "Antreprenoriat inovativ (curs)\nN.Nazar 528/3");
        context.AddOrMergeLesson(new(lesson,
            DayOfWeek.Monday,
            TimeSlot.First,
            [group],
            new("Gr.1")));
        context.AddOrMergeLesson(new(lesson,
            DayOfWeek.Monday,
            TimeSlot.First,
            [group],
            new("Gr.2")));

        Assert.Equal(expected: 2, actual: context.Schedule.WeeklyLessons.Count);
        Assert.Equal(expected: new[]
            {
                "Gr.1",
                "Gr.2",
            },
            actual: context.Schedule.WeeklyLessons.List
                .Select(l => l.Base.Group.Alternative.Value));
    }

    [Fact]
    public void ExplicitLessonGroupOverridesCellGroups()
    {
        var context = CreateContext();
        var cellGroup = context.Schedule.Group("IA2601(ro)").Id;
        var lessonGroup = context.Schedule.Group("IA2602(ro)").Id;
        var lesson = Parse(context, "HTML (lab)\nG.Marin 326/4");
        lesson.GroupName = "IA2602(ro)".AsMemory();

        context.AddOrMergeLesson(new(lesson,
            DayOfWeek.Tuesday,
            TimeSlot.First,
            [cellGroup]));

        var result = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(expected: lessonGroup, actual: Assert.Single(result.Base.Group.Groups));
        Assert.Equal(expected: DayOfWeek.Tuesday, actual: result.Date.DayOfWeek);
    }

    [Fact]
    public void UnknownLessonStartTimeStillThrowsDomainError()
    {
        var context = CreateContext();
        var group = context.Schedule.Group("IA2601(ro)").Id;
        var lesson = Parse(context, "HTML (lab)\nG.Marin 326/4");
        lesson.StartTime = new(hour: 7, minute: 13);

        Assert.Throws<InvalidScheduleDocumentException>(() =>
            context.AddOrMergeLesson(new(lesson,
                DayOfWeek.Monday,
                TimeSlot.First,
                [group])));
        Assert.Empty(context.Schedule.WeeklyLessons.List);
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
        context.Schedule.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2026),
        });
        context.SetPeriod(new()
        {
            StartDate = new(year: 2026,
                month: 9,
                day: 14),
        });
        return context;
    }

    private static ParsedLesson Parse(ScheduleImportContext context, string text)
    {
        using var lines = text.Split('\n').Select(x => x.AsMemory()).GetEnumerator();
        var parser = context.ParserFactory.Create();
        parser.Lexer.Reset(lines);
        return Assert.Single(parser.ParseLessons(new StringBuilder()));
    }
}

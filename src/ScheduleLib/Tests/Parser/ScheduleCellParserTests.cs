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
        context.AddOrMergeLesson(lesson, DayOfWeek.Monday, TimeSlot.First, [first]);
        context.AddOrMergeLesson(lesson, DayOfWeek.Monday, TimeSlot.First, [second]);

        var result = Assert.Single(context.Schedule.WeeklyLessons.List);
        Assert.Equal(2, result.Base.Group.Groups.Count);
        Assert.Equal(4, result.Date.TimeSlot!.Value.Index);
        Assert.Equal(Parity.OddWeek, result.Date.Parity);
        Assert.Equal(context.CurrentPeriodId, result.Base.General.Period);
    }

    [Fact]
    public void DifferentCohortAlternativesDoNotMergeSharedLectures()
    {
        var context = CreateContext();
        var group = context.Schedule.Group("IA2504(ru)").Id;
        var lesson = Parse(context, "Antreprenoriat inovativ (curs)\nN.Nazar 528/3");
        context.AddOrMergeLesson(lesson, DayOfWeek.Monday, TimeSlot.First, [group], new("Gr.1"));
        context.AddOrMergeLesson(lesson, DayOfWeek.Monday, TimeSlot.First, [group], new("Gr.2"));

        Assert.Equal(2, context.Schedule.WeeklyLessons.Count);
        Assert.Equal(new[] { "Gr.1", "Gr.2" }, context.Schedule.WeeklyLessons.List
            .Select(l => l.Base.Group.Alternative.Value));
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
        context.SetPeriod(new() { StartDate = new(2026, 9, 14) });
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

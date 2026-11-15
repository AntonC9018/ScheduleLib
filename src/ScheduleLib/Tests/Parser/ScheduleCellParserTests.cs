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
        var scheduleImportContext = context;
        ImportedLessonInput input = new(
            lesson,
            DayOfWeek.Monday,
            TimeSlot.First,
            [first]);
        scheduleImportContext.AddOrMergeLesson(input);
        var scheduleImportContext2 = context;
        ImportedLessonInput input2 = new(
            lesson,
            DayOfWeek.Monday,
            TimeSlot.First,
            [second]);
        scheduleImportContext2.AddOrMergeLesson(input2);

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
        var scheduleImportContext = context;
        var lesson2 = lesson;
        var day = DayOfWeek.Monday;
        var timeSlot = TimeSlot.First;
        IReadOnlyList<GroupId> groups = [group];
        Alternative alternative = new("Gr.1");
        ImportedLessonInput input = new(
            lesson2,
            day,
            timeSlot,
            groups,
            alternative);
        scheduleImportContext.AddOrMergeLesson(input);
        var scheduleImportContext2 = context;
        var lesson3 = lesson;
        var day2 = DayOfWeek.Monday;
        var timeSlot2 = TimeSlot.First;
        IReadOnlyList<GroupId> groups2 = [group];
        Alternative alternative2 = new("Gr.2");
        ImportedLessonInput input2 = new(
            lesson3,
            day2,
            timeSlot2,
            groups2,
            alternative2);
        scheduleImportContext2.AddOrMergeLesson(input2);

        Assert.Equal(expected: 2, actual: context.Schedule.WeeklyLessons.Count);
        Assert.Equal(
            expected: new[]
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

        var scheduleImportContext = context;
        ImportedLessonInput input = new(
            lesson,
            DayOfWeek.Tuesday,
            TimeSlot.First,
            [cellGroup]);
        scheduleImportContext.AddOrMergeLesson(input);

        var result = Assert.Single(context.Schedule.WeeklyLessons.List);
        var expected2 = lessonGroup;
        var actual2 = Assert.Single(result.Base.Group.Groups);
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: DayOfWeek.Tuesday, actual: result.Date.DayOfWeek);
    }

    [Fact]
    public void UnknownLessonStartTimeStillThrowsDomainError()
    {
        var context = CreateContext();
        var group = context.Schedule.Group("IA2601(ro)").Id;
        var lesson = Parse(context, "HTML (lab)\nG.Marin 326/4");
        lesson.StartTime = new(hour: 7, minute: 13);

        Assert.Throws<InvalidScheduleDocumentException.UnknownTimeSlotException>(() =>
        {
            var scheduleImportContext = context;
            ImportedLessonInput input = new(
                lesson,
                DayOfWeek.Monday,
                TimeSlot.First,
                [group]);
            scheduleImportContext.AddOrMergeLesson(input);
        });
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
            StartDate = new(
                year: 2026,
                month: 9,
                day: 14),
        });
        return context;
    }

    private static ParsedLesson Parse(ScheduleImportContext context, string text)
    {
        using var lines = text.Split('\n')
            .Select(x => x.AsMemory())
            .GetEnumerator();
        var parser = context.ParserFactory.Create();
        parser.Lexer.Reset(lines);
        var collection =
            parser.ParseLessons(new StringBuilder());
        var single = Assert.Single(collection);
        return single;
    }
}

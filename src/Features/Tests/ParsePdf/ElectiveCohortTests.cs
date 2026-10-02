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
        var context = ScheduleImportContext.Create(new()
        {
            DayNameProvider = new(), CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new()), SpecializationRegistry = Config.SpecializationRegistry,
        });
        context.Schedule.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var groups = new[] { "I2502(ru)", "IA2503(ru)", "IA2504(ru)" };
        var cohorts = new ElectiveCohorts(context,
        [
            new("test.pdf", 1, [groups[0], groups[2]], "Luni", "8:00", "", "Antreprenoriat Gr.1"),
            new("test.pdf", 1, [groups[1], groups[2]], "Luni", "8:00", "", "Antreprenoriat Gr.2"),
        ]);
        var ids = groups.Select(g => context.Schedule.Group(g).Id).ToArray();
        var lesson = new ParsedLesson
        {
            LessonName = "Antreprenoriat inovativ".AsMemory(),
            TeacherNames = [], RoomName = default, GroupName = default,
        };
        cohorts.Add(lesson, DayOfWeek.Monday, TimeSlot.First, ids, cohort: null);
        var lectures = context.Schedule.WeeklyLessons.List;
        Assert.Equal(2, lectures.Count);
        Assert.All(lectures, l => Assert.Equal(SubGroup.All, l.Base.Group.SubGroup));
        var first = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat Gr.1"));
        Assert.Equal(new[] { ids[0], ids[2] }, first.Base.Group.Groups.ToArray());
        var second = Assert.Single(lectures.Where(l => l.Base.Group.Alternative.Value == "Antreprenoriat Gr.2"));
        Assert.Equal(new[] { ids[1], ids[2] }, second.Base.Group.Groups.ToArray());
        Assert.Contains(Config.ImplicitSplitConfig.Scopes,
            s => s.AlternativeCourses.ContainsKey("Antreprenoriat inovativ"));
    }
}

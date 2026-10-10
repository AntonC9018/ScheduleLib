using ClosedXML.Excel;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryLabMergeTests
{
    [Fact]
    public void ExplicitMergeSelectsMondayLabsAndLeavesHistoricalWednesdayLessonsIntact()
    {
        var fixture = new Fixture();
        var selection = fixture.Select(fixture.Rules);
        var lesson = fixture.Schedule.Get(Assert.Single(selection.Lessons));
        Assert.Equal(DayOfWeek.Monday, lesson.Weekly!.Value.Date.DayOfWeek);
        Assert.Equal(GroupPartitionKey.All, selection.AttendancePartition);
        Assert.Equal(2, fixture.Schedule.EnumerateAllLessons().Count());
        Assert.Contains(fixture.Schedule.EnumerateAllLessons(), item => item.Weekly!.Value.Date.DayOfWeek == DayOfWeek.Wednesday);
    }

    [Fact]
    public void UnconfiguredGroupsDoNotAcquireImplicitMergeBehavior()
    {
        var fixture = new Fixture();
        Assert.Empty(fixture.Select([]).Lessons);
        Assert.Empty(fixture.Select([new() {Course="Other course",Group="IA2601"}]).Lessons);
        Assert.Empty(fixture.Select([new() {Course="Synthetic course",Group="IA2602"}]).Lessons);
    }

    [Fact]
    public void LabMergePreservesOrdinaryWholeGroupLectures()
    {
        var fixture = new Fixture(includeLecture: true);
        var selection = fixture.Select(fixture.Rules);
        Assert.Equal(2, selection.Lessons.Count);
        Assert.Contains(selection.Lessons, id => fixture.Schedule.Get(id).Lesson.Type == LessonType.Curs);
        Assert.DoesNotContain(selection.Lessons, id => fixture.Schedule.Get(id).Lesson.SubGroup == new SubGroup("II"));
    }

    [Fact]
    public void MissingSurvivingSubgroupStopsInsteadOfChoosingAnotherLesson()
    {
        var fixture = new Fixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Select(
            [new() {Course="Synthetic course",Group="IA2601",SourceSubGroup="III"}]));
    }

    [Fact]
    public void MergedWorkbookBuildsWholeGroupAttendanceUsingHistoricalSchedule()
    {
        var fixture = new Fixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601 (lab)");
        sheet.Cell(1,2).Value="lab";
        sheet.Cell(1,3).Value="lab";
        sheet.Cell(2,1).Value="Synthetic Alpha";
        sheet.Cell(2,2).Value="a";
        sheet.Cell(2,3).Value="a";
        sheet.Cell(2,4).Value="a"; // Outside the two declared lesson columns.
        sheet.Cell(3,1).Value="Synthetic Beta";
        sheet.Cell(3,2).Value="a";
        sheet.Cell(3,3).Value="a";
        var attendance=new AllStudentAttendanceListBuilder();
        var parser=new AttendanceListsExcelParser(fixture.Builder.GroupParseContext!,fixture.Lookup,
            LessonTypeParser.Instance,new SubGroupNameRemapper(fixture.Builder),SpecializationRegistry.Empty);
        parser.Parse(new(new(){LabMerges=fixture.Rules},attendance,fixture.Schedule.Filter(new()),workbook));
        var result=Assert.Single(attendance.Build(Attendance.Present));
        Assert.Equal(GroupPartitionKey.All,result.Key.GroupPartition);
        Assert.Equal(LessonType.Lab,result.Key.LessonType);
        Assert.Equal(2,result.Value.StudentNames.Count);
        Assert.Equal(2,result.Value.Attendance.Values.Count());
        Assert.All(result.Value.Attendance.Values, day => Assert.All(day.AsArray(), mark => Assert.Equal(Attendance.NotPresent,mark)));
    }

    private sealed class Fixture
    {
        internal readonly ScheduleBuilder Builder = new();
        internal readonly Schedule Schedule;
        internal readonly LookupFacade Lookup;
        private readonly CourseId _course;
        private readonly GroupId _group;
        internal readonly RegistryLabMergeRule[] Rules = [new(){Course="Synthetic course",Group="IA2601"}];

        internal Fixture(bool includeLecture = false)
        {
            Builder.GroupParseContext = GroupParseContext.Create(new(){CurrentStudyYear=new(2026)});
            Builder.EnableLookupModule();
            _course=Builder.Course("Synthetic course");
            _group=Builder.Group("IA2601");
            Add("I",DayOfWeek.Monday);
            Add("II",DayOfWeek.Wednesday);
            if (includeLecture)
            {
                Builder.RegularLesson(lesson =>
                {
                    lesson.Course(_course);
                    lesson.Group(_group);
                    lesson.Type(LessonType.Curs);
                    lesson.DayOfWeek(DayOfWeek.Friday);
                    lesson.TimeSlot(TimeSlot.First);
                });
            }
            var unifier=new CourseNameUnifierModule(Config.CourseNameUnifier);
            unifier.Refresh(Builder);
            Builder.RefreshLookup();
            Lookup=Builder.Lookup(unifier);
            Schedule=Builder.Build();
        }

        private void Add(string subgroup, DayOfWeek day)
        {
            Builder.RegularLesson(lesson=>
            {
                lesson.Course(_course);
                lesson.Group(_group);
                lesson.Type(LessonType.Lab);
                lesson.SubGroup(new(subgroup));
                lesson.DayOfWeek(day);
                lesson.TimeSlot(TimeSlot.First);
            });
        }

        internal RegistryLessonSelection Select(IReadOnlyList<RegistryLabMergeRule> rules) =>
            RegistryLabMerges.Select(rules,new(new LessonSearchFilter(_course,new FoundGroups(){Value=[_group],IsWildcard=false},GroupPartitionKey.All),
                Builder.LookupModule!.LessonsByCourse,Schedule));
    }
}

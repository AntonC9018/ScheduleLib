using ClosedXML.Excel;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class AutomaticLabSubGroupMergeTests
{
    [Theory]
    [InlineData("I")]
    [InlineData("II")]
    public void DerivesSurvivorWithoutManualChoiceAndReplacesEarlierDates(string survivor)
    {
        var fixture = new Fixture();
        fixture.Add(survivor, fixture.Latest, DayOfWeek.Thursday, fixture.NewTeacher);
        var raw = fixture.Build();
        Assert.Same(raw, LabSubGroupMerges.Project(raw, enabled: false));
        var merged = LabSubGroupMerges.Project(raw, enabled: true);
        var detection = Assert.Single(merged.AutomaticLabSubGroupMerges);
        Assert.Equal(new SubGroup(survivor), detection.SurvivingSubGroup);
        var lesson = Assert.Single(merged.EnumerateWeeklyLessons());
        Assert.Equal(SubGroup.All, lesson.Lesson.SubGroup);
        Assert.Equal(DayOfWeek.Thursday, lesson.Date.DayOfWeek);
        Assert.Equal(PeriodId.Unspecified, lesson.Date.Period);
        Assert.Equal(3, raw.EnumerateWeeklyLessons().Count());
        Assert.Equal(3, merged.WeeklyLessons.Length); // Stable raw lookup IDs.
        Assert.Empty(merged.Filter(FilterHelper.Builder().WithTeacher(fixture.OldTeacher)).Lessons);
        Assert.Single(merged.Filter(FilterHelper.Builder().WithTeacher(fixture.NewTeacher).WithLatestPeriod(merged)).Lessons);

        var weekly = new ManualWeeklyScheduledDateProvider(Config.StudyWeeks, Config.HolidayPeriods);
        var helper = new ScheduleDateProviderHelper(merged, LessonTimeConfig.CreateDefault(), Config.SemesterIntervalProvider());
        var dates = new ScheduledDateTimeProvider(weekly, helper);
        var events = new ScheduledTimeEventsProvider(new ScheduledEventsProviderTransformer(weekly), helper);
        var parameters = new GetDateTimesParams { Lessons = raw.EnumerateAllLessons().Select(x => x.Id), Semester = Semester.Sem1 };
        var occurrences = dates.Get(parameters).ToArray();
        Assert.NotEmpty(occurrences);
        Assert.True(occurrences[0].DateTime < fixture.Latest.Model.Start.ToDateTime(TimeOnly.MinValue));
        Assert.All(occurrences, x => Assert.Equal(lesson.Id.AsAny(), x.LessonId));
        var expanded = new List<LessonWithDate>();
        foreach (var timeEvent in events.Get(parameters))
            foreach (var date in timeEvent.Event)
                expanded.Add(new() { LessonId = timeEvent.LessonId, DateTime = date.ToDateTime(timeEvent.TimeInterval.Start) });
        Assert.Equal(occurrences.OrderBy(x => x.DateTime), expanded.OrderBy(x => x.DateTime));
    }

    [Fact]
    public void ExplicitWholeGroupLabsReplaceTheOldSplit()
    {
        var fixture = new Fixture();
        fixture.Add(null, fixture.Latest, DayOfWeek.Friday);
        var merged = LabSubGroupMerges.Project(fixture.Build(), true);
        Assert.Single(merged.AutomaticLabSubGroupMerges);
        Assert.Equal(DayOfWeek.Friday, Assert.Single(merged.EnumerateWeeklyLessons()).Date.DayOfWeek);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("new-label")]
    [InlineData("partial")]
    [InlineData("mixed")]
    [InlineData("overlap")]
    [InlineData("gap")]
    [InlineData("single-period")]
    [InlineData("undated")]
    public void InconclusiveEvidenceDoesNotGuess(string scenario)
    {
        var fixture = new Fixture();
        switch (scenario)
        {
            case "missing": fixture.Add(null, fixture.Latest, DayOfWeek.Friday, type: LessonType.Curs); break;
            case "new-label": fixture.Add("III", fixture.Latest, DayOfWeek.Friday); break;
            case "partial":
                fixture.Add("III", fixture.Older, DayOfWeek.Friday);
                fixture.Add("I", fixture.Latest, DayOfWeek.Monday);
                fixture.Add("II", fixture.Latest, DayOfWeek.Wednesday);
                break;
            case "mixed":
                fixture.Add("I", fixture.Latest, DayOfWeek.Monday);
                fixture.Add(null, fixture.Latest, DayOfWeek.Friday);
                break;
            case "overlap":
                fixture.Older.Model.EndExclusive = fixture.Latest.Model.Start.AddDays(1);
                fixture.Add("I", fixture.Latest, DayOfWeek.Monday);
                break;
            case "gap":
                fixture.Older.Model.EndExclusive = fixture.Latest.Model.Start.AddDays(-1);
                fixture.Add("I", fixture.Latest, DayOfWeek.Monday);
                break;
            case "undated": fixture.Add("I", PeriodId.Unspecified, DayOfWeek.Friday); break;
            case "single-period": break;
        }
        var raw = fixture.Build();
        var merged = LabSubGroupMerges.Project(raw, true);
        Assert.Empty(merged.AutomaticLabSubGroupMerges);
        Assert.Single(merged.LabSubGroupMergeDiagnostics);
        Assert.Equal(raw.EnumerateAllLessons().Select(x => x.Id), merged.EnumerateAllLessons().Select(x => x.Id));
        Assert.Equal(raw.WeeklyLessons.ToArray(), merged.WeeklyLessons.ToArray());
    }

    [Fact]
    public void RepeatedLatestSurvivorStillUsesEarlierSplitEvidence()
    {
        var fixture = new Fixture();
        fixture.Add("II", fixture.Latest, DayOfWeek.Thursday);
        var newest = fixture.Builder.Period(new(2026, 11, 1));
        fixture.Latest.Model.EndExclusive = newest.Model.Start;
        fixture.Add("II", newest, DayOfWeek.Friday);
        var merged = LabSubGroupMerges.Project(fixture.Build(), true);
        Assert.Single(merged.AutomaticLabSubGroupMerges);
        Assert.Equal(DayOfWeek.Friday, Assert.Single(merged.EnumerateWeeklyLessons()).Date.DayOfWeek);
    }

    [Fact]
    public void PreservesOtherCoursesGroupsTypesAndPartitionDimensions()
    {
        var fixture = new Fixture();
        var specialization = Specialization.CV;
        var alternative = new Alternative("Synthetic elective");
        fixture.Add("I", fixture.Latest, DayOfWeek.Thursday);
        fixture.Add(null, fixture.Older, DayOfWeek.Friday, type: LessonType.Curs);
        fixture.Add("I", fixture.Older, DayOfWeek.Friday, specialization: specialization, alternative: alternative);
        fixture.Add("II", fixture.Older, DayOfWeek.Saturday, specialization: specialization, alternative: alternative);
        fixture.Add("II", fixture.Latest, DayOfWeek.Saturday, specialization: specialization, alternative: alternative);
        fixture.Add(SpecialSubGroups.Beginners.Value, fixture.Older, DayOfWeek.Sunday);
        fixture.Add(SpecialSubGroups.NonBeginners.Value, fixture.Latest, DayOfWeek.Sunday);
        var otherGroup = fixture.Builder.Group("IA2602").Id;
        fixture.Add("I", fixture.Older, DayOfWeek.Monday, group: otherGroup);
        fixture.Add("II", fixture.Older, DayOfWeek.Wednesday, group: otherGroup);
        fixture.Add("I", fixture.Latest, DayOfWeek.Monday, group: otherGroup);
        fixture.Add("II", fixture.Latest, DayOfWeek.Wednesday, group: otherGroup);
        var otherCourse = fixture.Builder.Course("Other course");
        fixture.Add(null, fixture.Older, DayOfWeek.Tuesday, course: otherCourse);
        var raw = fixture.Build();
        var merged = LabSubGroupMerges.Project(raw, true);
        Assert.Equal(2, merged.AutomaticLabSubGroupMerges.Length);
        var partitioned = Assert.Single(merged.EnumerateWeeklyLessons().Where(x => x.Lesson.Specialization == specialization));
        Assert.Equal(new GroupPartitionKey(SubGroup.All, specialization, alternative), partitioned.Lesson.GroupPartitionKey);
        Assert.Equal(raw.EnumerateAllLessons().Count(x => x.Lesson.Group == otherGroup),
            merged.EnumerateAllLessons().Count(x => x.Lesson.Group == otherGroup));
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.Course == otherCourse);
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.Type == LessonType.Curs);
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.SubGroup == SpecialSubGroups.Beginners);
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.SubGroup == SpecialSubGroups.NonBeginners);
    }

    [Fact]
    public void AutomaticProjectionMatchesOrdinaryAndCombinedRegistryGroupsAndOverridesManualSurvivor()
    {
        var fixture = new Fixture();
        fixture.Add("II", fixture.Latest, DayOfWeek.Thursday);
        var merged = LabSubGroupMerges.Project(fixture.Build(), true);
        var search = new LessonSearchFilter(fixture.Course,
            new() { Value = [fixture.Group], IsWildcard = false }, GroupPartitionKey.All);
        var selection = RegistryLabMerges.Select([new() { Course = "Synthetic course", Group = "IA2601", SourceSubGroup = "I" }],
            new(search, fixture.Builder.LookupModule!.LessonsByCourse, merged));
        Assert.Equal(GroupPartitionKey.All, selection.AttendancePartition);
        Assert.Equal(new SubGroup("II"), Assert.Single(merged.AutomaticLabSubGroupMerges).SurvivingSubGroup);
        var id = Assert.Single(selection.Lessons);
        Assert.Equal(SubGroup.All, merged.Get(id).Lesson.SubGroup);
        var combined = new LessonSearchFilter(fixture.Course,
            new() { Value = [fixture.Group], IsWildcard = false, IsCombined = true }, GroupPartitionKey.All);
        Assert.Equal(selection.Lessons, RegistryLabMerges.Select([], new(combined, fixture.Builder.LookupModule.LessonsByCourse, merged)).Lessons);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601 (lab)");
        sheet.Cell(1, 2).Value = "lab";
        sheet.Cell(2, 1).Value = "Synthetic Alpha";
        sheet.Cell(2, 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        var parser = new AttendanceListsExcelParser(fixture.Builder.GroupParseContext!, fixture.Lookup,
            LessonTypeParser.Instance, new SubGroupNameRemapper(fixture.Builder), SpecializationRegistry.Empty);
        parser.Parse(new(new(), attendance, merged.Filter(new()), workbook));
        Assert.Equal(GroupPartitionKey.All, Assert.Single(attendance.Build(Attendance.Present)).Key.GroupPartition);
    }

    private sealed class Fixture
    {
        internal readonly ScheduleBuilder Builder = new();
        internal readonly CourseId Course;
        internal readonly GroupId Group;
        internal readonly TeacherId OldTeacher;
        internal readonly TeacherId NewTeacher;
        internal readonly PeriodBuilder Older;
        internal readonly PeriodBuilder Latest;
        internal LookupFacade Lookup = null!;

        internal Fixture()
        {
            Builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
            Builder.EnableLookupModule();
            Course = Builder.Course("Synthetic course");
            Group = Builder.Group("IA2601");
            OldTeacher = Builder.Teacher("Synthetic Alpha").Id;
            NewTeacher = Builder.Teacher("Synthetic Beta").Id;
            Older = Builder.Period(new(2026, 9, 1));
            Latest = Builder.Period(new(2026, 10, 11));
            Older.Model.EndExclusive = Latest.Model.Start;
            Add("I", Older, DayOfWeek.Monday);
            Add("II", Older, DayOfWeek.Wednesday);
        }

        internal void Add(string? subgroup, PeriodId period, DayOfWeek day, TeacherId? teacher = null,
            LessonType type = LessonType.Lab, Specialization specialization = default, Alternative alternative = default,
            GroupId? group = null, CourseId? course = null)
        {
            Builder.RegularLesson(lesson =>
            {
                lesson.Course(course ?? Course); lesson.Group(group ?? Group); lesson.SubGroup(new(subgroup));
                lesson.Specialization(specialization); lesson.Alternative(alternative); lesson.Type(type);
                lesson.Teacher(teacher ?? OldTeacher); lesson.DayOfWeek(day); lesson.TimeSlot(TimeSlot.First); lesson.Period(period);
            });
        }

        internal Schedule Build()
        {
            var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
            unifier.Refresh(Builder);
            Builder.RefreshLookup();
            Lookup = Builder.Lookup(unifier);
            return Builder.Build();
        }
    }
}

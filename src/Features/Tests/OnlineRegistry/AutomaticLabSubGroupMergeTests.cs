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
        fixture.Add(
            survivor,
            fixture.Latest,
            DayOfWeek.Thursday,
            fixture.NewTeacher);
        var raw = fixture.Build();
        object expected2 = raw;
        var actual2 = LabSubGroupMerges.Project(raw, enabled: false);
        object actual3 = actual2;
        Assert.Same(expected: expected2, actual: actual3);
        var merged = LabSubGroupMerges.Project(raw, enabled: true);
        var detection = Assert.Single(merged.AutomaticLabSubGroupMerges);
        var expected3 = new SubGroup(survivor);
        var actual4 = detection.SurvivingSubGroup;
        Assert.Equal(expected: expected3, actual: actual4);
        var lesson = Assert.Single(merged.EnumerateWeeklyLessons());
        Assert.Equal(expected: SubGroup.All, actual: lesson.Lesson.SubGroup);
        Assert.Equal(expected: DayOfWeek.Thursday, actual: lesson.Date.DayOfWeek);
        Assert.Equal(expected: PeriodId.Unspecified, actual: lesson.Date.Period);
        Assert.Equal(
            expected: 3,
            actual: raw.EnumerateWeeklyLessons()
                .Count());
        Assert.Equal(expected: 3, actual: merged.WeeklyLessons.Length); // Stable raw lookup IDs.

        ScheduleFilter CalculateFilter()
        {
            var filter = FilterHelper.Builder()
                .WithTeacher(fixture.OldTeacher);
            return filter;
        }

        var filter2 = merged.Filter(CalculateFilter());
        System.Collections.IEnumerable collection = filter2.Lessons;
        Assert.Empty(collection);
        Assert.Single(merged.Filter(FilterHelper.Builder()
            .WithTeacher(fixture.NewTeacher)
            .WithLatestPeriod(merged)).Lessons);

        var weekly = new ManualWeeklyScheduledDateProvider(Config.StudyWeeks, Config.HolidayPeriods);
        var helper = new ScheduleDateProviderHelper(
            merged,
            LessonTimeConfig.CreateDefault(),
            Config.SemesterIntervalProvider());
        var dates = new ScheduledDateTimeProvider(weekly, helper);
        var eventsProvider =
            new ScheduledEventsProviderTransformer(weekly);
        IWeeklyScheduledEventsProvider eventsProvider2 = eventsProvider;
        var helper2 = helper;
        var events = new ScheduledTimeEventsProvider(eventsProvider2, helper2);
        var parameters = new GetDateTimesParams
        {
            Lessons = raw.EnumerateAllLessons()
                .Select(x => x.Id),
            Semester = Semester.Sem1,
        };
        var occurrences = dates.Get(parameters)
            .ToArray();
        Assert.NotEmpty(occurrences);
        Assert.True(occurrences[0].DateTime < fixture.Latest.Model.Start.ToDateTime(TimeOnly.MinValue));
        Assert.All(occurrences, x => Assert.Equal(expected: lesson.Id.AsAny(), actual: x.LessonId));
        var expanded = new List<LessonWithDate>();
        foreach (var timeEvent in events.Get(parameters))
        {
            foreach (var date in timeEvent.Event)
            {
                expanded.Add(new()
                {
                    LessonId = timeEvent.LessonId,
                    DateTime = date.ToDateTime(timeEvent.TimeInterval.Start),
                });
            }
        }

        Assert.Equal(expected: occurrences.OrderBy(x => x.DateTime), actual: expanded.OrderBy(x => x.DateTime));
    }

    [Fact]
    public void ExplicitWholeGroupLabsReplaceTheOldSplit()
    {
        var fixture = new Fixture();
        fixture.Add(
            null,
            fixture.Latest,
            DayOfWeek.Friday);
        var merged = LabSubGroupMerges.Project(fixture.Build(), true);
        Assert.Single(merged.AutomaticLabSubGroupMerges);
        Assert.Equal(expected: DayOfWeek.Friday, actual: Assert.Single(merged.EnumerateWeeklyLessons()).Date.DayOfWeek);
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
            case "missing":
                fixture.Add(
                    null,
                    fixture.Latest,
                    DayOfWeek.Friday,
                    type: LessonType.Curs);
                break;
            case "new-label":
                fixture.Add(
                    "III",
                    fixture.Latest,
                    DayOfWeek.Friday);
                break;
            case "partial":
                fixture.Add(
                    "III",
                    fixture.Older,
                    DayOfWeek.Friday);
                fixture.Add(
                    "I",
                    fixture.Latest,
                    DayOfWeek.Monday);
                fixture.Add(
                    "II",
                    fixture.Latest,
                    DayOfWeek.Wednesday);
                break;
            case "mixed":
                fixture.Add(
                    "I",
                    fixture.Latest,
                    DayOfWeek.Monday);
                fixture.Add(
                    null,
                    fixture.Latest,
                    DayOfWeek.Friday);
                break;
            case "overlap":
                fixture.Older.Model.EndExclusive = fixture.Latest.Model.Start.AddDays(1);
                fixture.Add(
                    "I",
                    fixture.Latest,
                    DayOfWeek.Monday);
                break;
            case "gap":
                fixture.Older.Model.EndExclusive = fixture.Latest.Model.Start.AddDays(-1);
                fixture.Add(
                    "I",
                    fixture.Latest,
                    DayOfWeek.Monday);
                break;
            case "undated":
                fixture.Add(
                    "I",
                    PeriodId.Unspecified,
                    DayOfWeek.Friday);
                break;
            case "single-period":
                break;
        }
        var raw = fixture.Build();
        var merged = LabSubGroupMerges.Project(raw, true);
        Assert.Empty(merged.AutomaticLabSubGroupMerges);
        Assert.Single(merged.LabSubGroupMergeDiagnostics);
        Assert.Equal(
            expected: raw.EnumerateAllLessons()
                .Select(x => x.Id),
            actual: merged.EnumerateAllLessons()
                .Select(x => x.Id));
        Assert.Equal(expected: raw.WeeklyLessons.ToArray(), actual: merged.WeeklyLessons.ToArray());
    }

    [Fact]
    public void RepeatedLatestSurvivorStillUsesEarlierSplitEvidence()
    {
        var fixture = new Fixture();
        fixture.Add(
            "II",
            fixture.Latest,
            DayOfWeek.Thursday);
        var scheduleBuilder = fixture.Builder;
        DateOnly start = new(
            year: 2026,
            month: 11,
            day: 1);
        var newest = scheduleBuilder.Period(start);
        fixture.Latest.Model.EndExclusive = newest.Model.Start;
        fixture.Add(
            "II",
            newest,
            DayOfWeek.Friday);
        var merged = LabSubGroupMerges.Project(fixture.Build(), true);
        Assert.Single(merged.AutomaticLabSubGroupMerges);
        Assert.Equal(expected: DayOfWeek.Friday, actual: Assert.Single(merged.EnumerateWeeklyLessons()).Date.DayOfWeek);
    }

    [Fact]
    public void PreservesOtherCoursesGroupsTypesAndPartitionDimensions()
    {
        var fixture = new Fixture();
        var specialization = Specialization.CV;
        var alternative = new Alternative("Synthetic elective");
        fixture.Add(
            "I",
            fixture.Latest,
            DayOfWeek.Thursday);
        fixture.Add(
            null,
            fixture.Older,
            DayOfWeek.Friday,
            type: LessonType.Curs);
        fixture.Add(
            "I",
            fixture.Older,
            DayOfWeek.Friday,
            specialization: specialization,
            alternative: alternative);
        fixture.Add(
            "II",
            fixture.Older,
            DayOfWeek.Saturday,
            specialization: specialization,
            alternative: alternative);
        fixture.Add(
            "II",
            fixture.Latest,
            DayOfWeek.Saturday,
            specialization: specialization,
            alternative: alternative);
        fixture.Add(
            SpecialSubGroups.Beginners.Value,
            fixture.Older,
            DayOfWeek.Sunday);
        fixture.Add(
            SpecialSubGroups.NonBeginners.Value,
            fixture.Latest,
            DayOfWeek.Sunday);
        var otherGroup = fixture.Builder.Group("IA2602").Id;
        fixture.Add(
            "I",
            fixture.Older,
            DayOfWeek.Monday,
            group: otherGroup);
        fixture.Add(
            "II",
            fixture.Older,
            DayOfWeek.Wednesday,
            group: otherGroup);
        fixture.Add(
            "I",
            fixture.Latest,
            DayOfWeek.Monday,
            group: otherGroup);
        fixture.Add(
            "II",
            fixture.Latest,
            DayOfWeek.Wednesday,
            group: otherGroup);
        var otherCourse = fixture.Builder.Course("Other course");
        fixture.Add(
            null,
            fixture.Older,
            DayOfWeek.Tuesday,
            course: otherCourse);
        var raw = fixture.Build();
        var merged = LabSubGroupMerges.Project(raw, true);
        Assert.Equal(expected: 2, actual: merged.AutomaticLabSubGroupMerges.Length);
        var partitioned = Assert.Single(merged.EnumerateWeeklyLessons()
            .Where(x => x.Lesson.Specialization == specialization));
        var expected2 = new GroupPartitionKey(
            SubGroup.All,
            specialization,
            alternative);
        var actual2 = partitioned.Lesson.GroupPartitionKey;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(
            expected: raw.EnumerateAllLessons()
                .Count(x => x.Lesson.Group == otherGroup),
            actual: merged.EnumerateAllLessons()
                .Count(x => x.Lesson.Group == otherGroup));
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.Course == otherCourse);
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.Type == LessonType.Curs);
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.SubGroup == SpecialSubGroups.Beginners);
        Assert.Contains(merged.EnumerateAllLessons(), x => x.Lesson.SubGroup == SpecialSubGroups.NonBeginners);
    }

    [Fact]
    public void AutomaticProjectionMatchesOrdinaryAndCombinedRegistryGroupsAndOverridesManualSurvivor()
    {
        var fixture = new Fixture();
        fixture.Add(
            "II",
            fixture.Latest,
            DayOfWeek.Thursday);
        var merged = LabSubGroupMerges.Project(fixture.Build(), true);
        var search = new LessonSearchFilter(
            fixture.Course,
            new()
            {
                Value = [fixture.Group],
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var selection = RegistryLabMerges.Select(
            [
                new()
                {
                    Course = "Synthetic course",
                    Group = "IA2601",
                    SourceSubGroup = "I",
                },
            ],
            new(
                search,
                fixture.Builder.LookupModule!.LessonsByCourse,
                merged));
        Assert.Equal(expected: GroupPartitionKey.All, actual: selection.AttendancePartition);

        SubGroup CalculateExpected()
        {
            var expected2 = new SubGroup("II");
            return expected2;
        }

        Assert.Equal(
            expected: CalculateExpected(),
            actual: Assert.Single(merged.AutomaticLabSubGroupMerges).SurvivingSubGroup);
        var id = Assert.Single(selection.Lessons);
        Assert.Equal(expected: SubGroup.All, actual: merged.Get(id).Lesson.SubGroup);
        var combined = new LessonSearchFilter(
            fixture.Course,
            new()
            {
                Value = [fixture.Group],
                IsWildcard = false,
                IsCombined = true,
            },
            GroupPartitionKey.All);
        Assert.Equal(
            expected: selection.Lessons,
            actual: RegistryLabMerges.Select(
                [],
                new(
                    combined,
                    fixture.Builder.LookupModule.LessonsByCourse,
                    merged)).Lessons);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601 (lab)");
        sheet.Cell(row: 1, column: 2).Value = "lab";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Alpha";
        sheet.Cell(row: 2, column: 2).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        var groupParser = fixture.Builder.GroupParseContext!;
        var lookup = fixture.Lookup;
        var lessonTypeParser = LessonTypeParser.Instance;
        var subGroupRemapper = new SubGroupNameRemapper(fixture.Builder);
        var specializationRegistry = SpecializationRegistry.Empty;
        var parser = new AttendanceListsExcelParser(
            groupParser,
            lookup,
            lessonTypeParser,
            subGroupRemapper,
            specializationRegistry);
        parser.Parse(new(
            new(),
            attendance,
            merged.Filter(new()),
            workbook));

        StudentAttendanceList CalculateCollection()
        {
            var collection = attendance.Build(Attendance.Present);
            return collection;
        }

        Assert.Equal(expected: GroupPartitionKey.All, actual: Assert.Single(CalculateCollection()).Key.GroupPartition);
    }

    [Theory]
    [InlineData("I")]
    [InlineData("II")]
    [InlineData(null)]
    public void CanonicalSubgroupSheetsRemainReadableAfterAutomaticMerge(string? survivor)
    {
        var fixture = new Fixture();
        fixture.Add(
            survivor,
            fixture.Latest,
            DayOfWeek.Monday);
        var schedule = LabSubGroupMerges.Project(fixture.Build(), true);
        using var workbook = new XLWorkbook();
        foreach (var subgroup in new[]
                 {
                     "I",
                     "II",
                 })
        {
            var xLWorkbook = workbook;
            string sheetName = $"IA2601 {subgroup}";
            var sheet = xLWorkbook.AddWorksheet(sheetName);
            sheet.Cell(row: 1, column: 2).Value = "lab";
            sheet.Cell(row: 2, column: 1).Value = subgroup == "I" ? "Synthetic Alpha" : "Synthetic Beta";
            sheet.Cell(row: 2, column: 2).Value = subgroup == "I" ? "a" : "am";
        }
        var builder = new AllStudentAttendanceListBuilder();
        var groupParser = fixture.Builder.GroupParseContext!;
        var lookup = fixture.Lookup;
        var lessonTypeParser = LessonTypeParser.Instance;
        var subGroupRemapper = new SubGroupNameRemapper(fixture.Builder);
        var specializationRegistry = SpecializationRegistry.Empty;
        var parser = new AttendanceListsExcelParser(
            groupParser,
            lookup,
            lessonTypeParser,
            subGroupRemapper,
            specializationRegistry);
        parser.Parse(new(
            new()
            {
                WorksheetNames = new Dictionary<string, string>
                {
                    ["IA2601 I"] = "Synthetic course IA2601 I (lab)",
                    ["IA2601 II"] = "Synthetic course IA2601 II (lab)",
                },
            },
            builder,
            schedule.Filter(new()),
            workbook));
        var attendance = builder.Build(Attendance.Present);
        Assert.Equal(expected: 2, actual: attendance.Count());
        Assert.All(
            attendance,
            entry => Assert.NotEqual(expected: SubGroup.All, actual: entry.Key.GroupPartition.SubGroup));
        var projected = AttendanceLabMerges.Project(
            attendance,
            schedule,
            []);
        var merged = Assert.Single(projected.Where(entry => entry.Key.GroupPartition.SubGroup == SubGroup.All));
        Assert.Equal(expected: 2, actual: merged.Value.StudentNames.Count);
        Assert.Equal(
            expected: new[]
            {
                Attendance.NotPresent,
                Attendance.MotivatedAbsent,
            },
            actual: merged.Value.Attendance.Day(0)
                .AsArray());
    }

    [Theory]
    [InlineData("III", true)]
    [InlineData("II", false)]
    public void UnprovenSubgroupsAndOtherTeacherDestinationsRemainUnmatched(string subgroup, bool selectedTeacher)
    {
        var fixture = new Fixture();
        fixture.Add(
            "I",
            fixture.Latest,
            DayOfWeek.Monday,
            fixture.NewTeacher);
        var schedule = LabSubGroupMerges.Project(fixture.Build(), true);
        using var workbook = new XLWorkbook();
        var xLWorkbook = workbook;
        string sheetName = $"IA2601 {subgroup}";
        var sheet = xLWorkbook.AddWorksheet(sheetName);
        sheet.Cell(row: 1, column: 2).Value = "lab";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Alpha";
        sheet.Cell(row: 2, column: 2).Value = "a";
        var groupParser = fixture.Builder.GroupParseContext!;
        var lookup = fixture.Lookup;
        var lessonTypeParser = LessonTypeParser.Instance;
        var subGroupRemapper = new SubGroupNameRemapper(fixture.Builder);
        var specializationRegistry = SpecializationRegistry.Empty;
        var parser = new AttendanceListsExcelParser(
            groupParser,
            lookup,
            lessonTypeParser,
            subGroupRemapper,
            specializationRegistry);

        ScheduleFilter CalculateFilter()
        {
            var filter = FilterHelper.Builder()
                .WithTeacher(fixture.OldTeacher);
            return filter;
        }

        var filtered = selectedTeacher ? schedule.Filter(new()) : schedule.Filter(CalculateFilter());
        Assert.Throws<AttendanceLessonNotFoundException>(() => parser.Parse(new(
            new()
            {
                WorksheetNames =
                    new Dictionary<string, string>
                    {
                        [sheet.Name] = $"Synthetic course IA2601 {subgroup} (lab)",
                    },
            },
            new AllStudentAttendanceListBuilder(),
            filtered,
            workbook)));
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
            Builder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
            Builder.EnableLookupModule();
            Course = Builder.Course("Synthetic course");
            Group = Builder.Group("IA2601");
            OldTeacher = Builder.Teacher("Synthetic Alpha").Id;
            NewTeacher = Builder.Teacher("Synthetic Beta").Id;
            var scheduleBuilder = Builder;
            DateOnly start = new(
                year: 2026,
                month: 9,
                day: 1);
            var period = scheduleBuilder.Period(start);
            Older = period;
            var scheduleBuilder2 = Builder;
            DateOnly start2 = new(
                year: 2026,
                month: 10,
                day: 11);
            var period2 = scheduleBuilder2.Period(start2);
            Latest = period2;
            Older.Model.EndExclusive = Latest.Model.Start;
            Add(
                "I",
                Older,
                DayOfWeek.Monday);
            Add(
                "II",
                Older,
                DayOfWeek.Wednesday);
        }

        internal void Add(
            string? subgroup,
            PeriodId period,
            DayOfWeek day,
            TeacherId? teacher = null,
            LessonType type = LessonType.Lab,
            Specialization specialization = default,
            Alternative alternative = default,
            GroupId? group = null,
            CourseId? course = null)
        {
            Builder.RegularLesson(lesson =>
            {
                lesson.Course(course ?? Course);
                lesson.Group(group ?? Group);
                var
                    lessonBuilder = lesson;
                SubGroup subGroup = new(subgroup);
                lessonBuilder.SubGroup(subGroup);
                lesson.Specialization(specialization);
                lesson.Alternative(alternative);
                lesson.Type(type);
                lesson.Teacher(teacher ?? OldTeacher);
                lesson.DayOfWeek(day);
                lesson.TimeSlot(TimeSlot.First);
                lesson.Period(period);
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

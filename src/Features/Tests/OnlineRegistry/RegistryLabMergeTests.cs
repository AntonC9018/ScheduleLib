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
        global::ScheduleLib.Schedule schedule = fixture.Schedule;
        global::ScheduleLib.AnyLessonId id = Assert.Single(selection.Lessons);
        var lesson = schedule.Get(id);
        Assert.Equal(expected: DayOfWeek.Monday, actual: lesson.Weekly!.Value.Date.DayOfWeek);
        Assert.Equal(expected: GroupPartitionKey.All, actual: selection.AttendancePartition);
        Assert.Equal(expected: 2, actual: fixture.Schedule.EnumerateAllLessons()
            .Count());
        Assert.Contains(fixture.Schedule.EnumerateAllLessons(), item => item.Weekly!.Value.Date.DayOfWeek == DayOfWeek.Wednesday);
    }

    [Fact]
    public void UnconfiguredGroupsDoNotAcquireImplicitMergeBehavior()
    {
        var fixture = new Fixture();
        Assert.Empty(fixture.Select([]).Lessons);
        Assert.Empty(fixture.Select([new() {
        Course="Other course",
        Group="IA2601"
        }]).Lessons);
        Assert.Empty(fixture.Select([new() {
        Course="Synthetic course",
        Group="IA2602"
        }]).Lessons);
    }

    [Fact]
    public void LabMergePreservesOrdinaryWholeGroupLectures()
    {
        var fixture = new Fixture(includeLecture: true);
        var selection = fixture.Select(fixture.Rules);
        Assert.Equal(expected: 2, actual: selection.Lessons.Count);
        Assert.Contains(selection.Lessons, id => fixture.Schedule.Get(id).Lesson.Type == LessonType.Curs);
        Assert.DoesNotContain(selection.Lessons, id => fixture.Schedule.Get(id).Lesson.SubGroup == new SubGroup("II"));
    }

    [Fact]
    public void MissingSurvivingSubgroupStopsInsteadOfChoosingAnotherLesson()
    {
        var fixture = new Fixture();
        Assert.Throws<InvalidOperationException>(() => fixture.Select(
            [new() {
            Course="Synthetic course",
            Group="IA2601",
            SourceSubGroup="III"
            }]));
    }

    [Fact]
    public void MergedWorkbookBuildsWholeGroupAttendanceUsingHistoricalSchedule()
    {
        var fixture = new Fixture();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Synthetic course IA2601 (lab)");
        sheet.Cell(row: 1, column: 2).Value = "lab";
        sheet.Cell(row: 1, column: 3).Value = "lab";
        sheet.Cell(row: 2, column: 1).Value = "Synthetic Alpha";
        sheet.Cell(row: 2, column: 2).Value = "a";
        sheet.Cell(row: 2, column: 3).Value = "a";
        sheet.Cell(row: 2, column: 4).Value = "a"; // Outside the two declared lesson columns.
        sheet.Cell(row: 3, column: 1).Value = "Synthetic Beta";
        sheet.Cell(row: 3, column: 2).Value = "a";
        sheet.Cell(row: 3, column: 3).Value = "a";
        var attendance = new AllStudentAttendanceListBuilder();
        global::ScheduleLib.Parsing.GroupParser.GroupParseContext groupParser = fixture.Builder.GroupParseContext!;
        global::ScheduleLib.Builders.LookupFacade lookup = fixture.Lookup;
        global::ScheduleLib.Parsing.Lesson.LessonTypeParser lessonTypeParser = LessonTypeParser.Instance;
        global::ScheduleLib.Builders.SubGroupNameRemapper subGroupRemapper = new SubGroupNameRemapper(fixture.Builder);
        global::ScheduleLib.SpecializationRegistry specializationRegistry = SpecializationRegistry.Empty;
        var parser = new AttendanceListsExcelParser(groupParser, lookup, lessonTypeParser, subGroupRemapper, specializationRegistry);
        global::ScheduleLib.FilteredSchedule CalculateSchedule()
        {
            global::ScheduleLib.FilteredSchedule schedule = fixture.Schedule.Filter(new());
            return schedule;
        }

        parser.Parse(new(new() { LabMerges = fixture.Rules }, attendance, CalculateSchedule(), workbook));
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList collection = attendance.Build(Attendance.Present);
        global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<global::ScheduleLib.OnlineRegistry.StudentsLookupKey, global::ScheduleLib.OnlineRegistry.StudentAttendanceList.BetterAttendances>> collection2 = collection;
        var result = Assert.Single(collection2);
        Assert.Equal(expected: GroupPartitionKey.All, actual: result.Key.GroupPartition);
        Assert.Equal(expected: LessonType.Lab, actual: result.Key.LessonType);
        Assert.Equal(expected: 2, actual: result.Value.StudentNames.Count);
        Assert.Equal(expected: 2, actual: result.Value.Attendance.Values.Count());
        Assert.All(result.Value.Attendance.Values, day => Assert.All(day.AsArray(), mark => Assert.Equal(expected: Attendance.NotPresent, actual: mark)));
    }

    [Fact]
    public void CanonicalSheetsComposeRowsByLessonOrdinalOnlyForConfiguredLabs()
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        global::ScheduleLib.GroupPartitionKey CalculateGroupPartition()
        {
            global::ScheduleLib.SubGroup subGroup = new SubGroup("I");
            global::ScheduleLib.Specialization specialization = Specialization.All;
            global::ScheduleLib.GroupPartitionKey groupPartition = new(subGroup, specialization);
            return groupPartition;
        }

        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key = new([fixture.Group], CalculateGroupPartition(), fixture.Course, LessonType.Lab);
            return key;
        }

        var first = builder.List(CalculateKey());
        global::ScheduleLib.Parsing.Name CalculateName()
        {
            global::ScheduleLib.Parsing.Name name2 = ScheduleLib.Parsing.NameHelper.Parse("Synthetic Alpha");
            return name2;
        }

        first.Student(CalculateName())
            .Day(Attendance.NotPresent);
        first.HintMaxCount(1);
        global::ScheduleLib.GroupPartitionKey CalculateGroupPartition2()
        {
            global::ScheduleLib.SubGroup subGroup2 = new SubGroup("II");
            global::ScheduleLib.Specialization specialization2 = Specialization.All;
            global::ScheduleLib.GroupPartitionKey groupPartition2 = new(subGroup2, specialization2);
            return groupPartition2;
        }

        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey2()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key2 = new([fixture.Group], CalculateGroupPartition2(), fixture.Course, LessonType.Lab);
            return key2;
        }

        var second = builder.List(CalculateKey2());
        global::ScheduleLib.OnlineRegistry.StudentAttendanceListBuilder studentAttendanceListBuilder = second;
        global::ScheduleLib.Parsing.Name name = ScheduleLib.Parsing.NameHelper.Parse("Synthetic Beta");
        var student = studentAttendanceListBuilder.Student(name);
        student.Day(Attendance.MotivatedAbsent);
        student.Day(Attendance.NotPresent);
        var canonical = builder.Build(Attendance.Present);
        Assert.Equal(expected: 2, actual: AttendanceLabMerges.Project(canonical, fixture.Schedule, [])
            .Count());
        var result = AttendanceLabMerges.Project(canonical, fixture.Schedule, fixture.Rules);
        var merged = Assert.Single(result.Where(entry => entry.Key.GroupPartition == GroupPartitionKey.All));
        Assert.Equal(expected: new[] { Attendance.NotPresent, Attendance.MotivatedAbsent }, actual: merged.Value.Attendance.Day(0)
            .AsArray());
        Assert.Equal(expected: new[] { Attendance.None, Attendance.NotPresent }, actual: merged.Value.Attendance.Day(1)
            .AsArray());
        Assert.Equal(expected: 2, actual: merged.Value.StudentNames.Count);
    }

    [Fact]
    public void DatedCanonicalSheetsPreserveDifferentDatesAndAmbiguousSourceColumns()
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        global::ScheduleLib.GroupPartitionKey CalculateGroupPartition()
        {
            global::ScheduleLib.SubGroup subGroup = new SubGroup("I");
            global::ScheduleLib.Specialization specialization = Specialization.All;
            global::ScheduleLib.GroupPartitionKey groupPartition = new(subGroup, specialization);
            return groupPartition;
        }

        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key = new([fixture.Group], CalculateGroupPartition(), fixture.Course, LessonType.Lab);
            return key;
        }

        var first = builder.List(CalculateKey());
        first.AddAttendanceDates([new() {
        Month = 9,
        Day = 1
        }, new() {
        Month = 9,
        Day = 8
        }]);
        global::ScheduleLib.OnlineRegistry.StudentAttendanceListBuilder studentAttendanceListBuilder = first;
        global::ScheduleLib.Parsing.Name name = ScheduleLib.Parsing.NameHelper.Parse("Synthetic Alpha");
        var alpha = studentAttendanceListBuilder.Student(name);
        alpha.Day(Attendance.NotPresent);
        alpha.Day(Attendance.MotivatedAbsent);
        global::ScheduleLib.GroupPartitionKey CalculateGroupPartition2()
        {
            global::ScheduleLib.SubGroup subGroup2 = new SubGroup("II");
            global::ScheduleLib.Specialization specialization2 = Specialization.All;
            global::ScheduleLib.GroupPartitionKey groupPartition2 = new(subGroup2, specialization2);
            return groupPartition2;
        }

        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey2()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key2 = new([fixture.Group], CalculateGroupPartition2(), fixture.Course, LessonType.Lab);
            return key2;
        }

        var second = builder.List(CalculateKey2());
        second.AddAttendanceDates([new() {
        Month = 9,
        Day = 2
        }, new() {
        Month = 9,
        Day = 8
        }, new() {
        Month = 9,
        Day = 8
        }]);
        global::ScheduleLib.OnlineRegistry.StudentAttendanceListBuilder studentAttendanceListBuilder2 = second;
        global::ScheduleLib.Parsing.Name name2 = ScheduleLib.Parsing.NameHelper.Parse("Synthetic Beta");
        var beta = studentAttendanceListBuilder2.Student(name2);
        beta.Day(Attendance.MotivatedAbsent);
        beta.Day(Attendance.NotPresent);
        beta.Day(Attendance.NotPresent);
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList attendance = builder.Build(Attendance.Present);
        global::ScheduleLib.Schedule schedule = fixture.Schedule;
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.OnlineRegistry.RegistryLabMergeRule> rules = fixture.Rules;
        var result = AttendanceLabMerges.Project(attendance, schedule, rules);
        var merged = Assert.Single(result.Where(entry => entry.Key.GroupPartition == GroupPartitionKey.All));
        Assert.Equal(expected: 3, actual: merged.Value.Dates!.Value.Length);
        Assert.Equal(expected: new[] { Attendance.NotPresent, Attendance.None }, actual: merged.Value.Attendance.Day(0)
            .AsArray());
        Assert.Equal(expected: new[] { Attendance.None, Attendance.MotivatedAbsent }, actual: merged.Value.Attendance.Day(1)
            .AsArray());
        Assert.Equal(expected: new[] { Attendance.MotivatedAbsent, Attendance.None }, actual: merged.Value.Attendance.Day(2)
            .AsArray());
    }

    [Fact]
    public void ExistingWholeGroupSheetTakesPrecedenceOverCanonicalSheets()
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        global::ScheduleLib.GroupPartitionKey CalculateGroupPartition()
        {
            global::ScheduleLib.SubGroup subGroup = new SubGroup("I");
            global::ScheduleLib.Specialization specialization = Specialization.All;
            global::ScheduleLib.GroupPartitionKey groupPartition = new(subGroup, specialization);
            return groupPartition;
        }

        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key = new([fixture.Group], CalculateGroupPartition(), fixture.Course, LessonType.Lab);
            return key;
        }

        global::ScheduleLib.Parsing.Name CalculateName()
        {
            global::ScheduleLib.Parsing.Name name = ScheduleLib.Parsing.NameHelper.Parse("Synthetic Alpha");
            return name;
        }

        builder.List(CalculateKey())
                                    .Student(CalculateName())
            .Day(Attendance.NotPresent);
        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey2()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key2 = new([fixture.Group], GroupPartitionKey.All, fixture.Course, LessonType.Lab);
            return key2;
        }

        global::ScheduleLib.Parsing.Name CalculateName2()
        {
            global::ScheduleLib.Parsing.Name name2 = ScheduleLib.Parsing.NameHelper.Parse("Synthetic Alpha");
            return name2;
        }

        builder.List(CalculateKey2())
                            .Student(CalculateName2())
            .Day(Attendance.MotivatedAbsent);
        global::ScheduleLib.OnlineRegistry.StudentAttendanceList attendance = builder.Build(Attendance.Present);
        global::ScheduleLib.Schedule schedule = fixture.Schedule;
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.OnlineRegistry.RegistryLabMergeRule> rules = fixture.Rules;
        var result = AttendanceLabMerges.Project(attendance, schedule, rules);
        var merged = Assert.Single(result.Where(entry => entry.Key.GroupPartition == GroupPartitionKey.All));
        Assert.Equal(expected: Attendance.MotivatedAbsent, actual: Assert.Single(merged.Value.Attendance.Day(0)
            .AsArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingSubgroupSourcesAreRejected(bool duplicateName)
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        global::ScheduleLib.GroupPartitionKey CalculateGroupPartition()
        {
            global::ScheduleLib.SubGroup subGroup = new SubGroup("I");
            global::ScheduleLib.Specialization specialization = Specialization.All;
            global::ScheduleLib.GroupPartitionKey groupPartition = new(subGroup, specialization);
            return groupPartition;
        }

        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key = new([fixture.Group], CalculateGroupPartition(), fixture.Course, LessonType.Lab);
            return key;
        }

        var first = builder.List(CalculateKey());
        global::ScheduleLib.Parsing.Name CalculateName()
        {
            global::ScheduleLib.Parsing.Name name = ScheduleLib.Parsing.NameHelper.Parse("Synthetic Alpha");
            return name;
        }

        first.Student(CalculateName())
            .Day(Attendance.NotPresent);
        global::ScheduleLib.GroupPartitionKey CalculateGroupPartition2()
        {
            global::ScheduleLib.SubGroup subGroup2 = new SubGroup("II");
            global::ScheduleLib.Specialization specialization2 = Specialization.All;
            global::ScheduleLib.GroupPartitionKey groupPartition2 = new(subGroup2, specialization2);
            return groupPartition2;
        }

        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey2()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key2 = new([fixture.Group], CalculateGroupPartition2(), fixture.Course, LessonType.Lab);
            return key2;
        }

        var second = builder.List(CalculateKey2());
        global::ScheduleLib.Parsing.Name CalculateName2()
        {
            string s;
            if (duplicateName)
            {
                s = "Synthetic Alpha";
            }
            else
            {
                s = "Synthetic Beta";
            }

            global::ScheduleLib.Parsing.Name name2 = ScheduleLib.Parsing.NameHelper.Parse(s);
            return name2;
        }

        second.Student(CalculateName2())
            .Day(Attendance.MotivatedAbsent);
        if (!duplicateName)
        {
            second.AddAttendanceDates([new() {
        Month = 9,
        Day = 1
        }]);
        }

        Assert.Throws<InvalidOperationException>(() =>
        {
            global::ScheduleLib.OnlineRegistry.StudentAttendanceList attendance = builder.Build(Attendance.Present);
            global::ScheduleLib.Schedule schedule = fixture.Schedule;
            global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.OnlineRegistry.RegistryLabMergeRule> rules = fixture.Rules;
            global::ScheduleLib.OnlineRegistry.StudentAttendanceList project = AttendanceLabMerges.Project(attendance, schedule, rules);
            return project;
        });
    }

    private sealed class Fixture
    {
        internal readonly ScheduleBuilder Builder = new();
        internal readonly Schedule Schedule;
        internal readonly LookupFacade Lookup;
        internal CourseId Course => _course;
        internal GroupId Group => _group;
        private readonly CourseId _course;
        private readonly GroupId _group;
        internal readonly RegistryLabMergeRule[] Rules = [new(){
        Course="Synthetic course",
        Group="IA2601"
        }];

        internal Fixture(bool includeLecture = false)
        {
            Builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
            Builder.EnableLookupModule();
            _course = Builder.Course("Synthetic course");
            _group = Builder.Group("IA2601");
            Add("I", DayOfWeek.Monday);
            Add("II", DayOfWeek.Wednesday);
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
            var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
            unifier.Refresh(Builder);
            Builder.RefreshLookup();
            Lookup = Builder.Lookup(unifier);
            Schedule = Builder.Build();
        }

        private void Add(string subgroup, DayOfWeek day)
        {
            Builder.RegularLesson(lesson =>
            {
                lesson.Course(_course);
                lesson.Group(_group);
                lesson.Type(LessonType.Lab);
                global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder = lesson;
                global::ScheduleLib.SubGroup subGroup = new(subgroup);
                lessonBuilder.SubGroup(subGroup);
                lesson.DayOfWeek(day);
                lesson.TimeSlot(TimeSlot.First);
            });
        }

        internal RegistryLessonSelection Select(IReadOnlyList<RegistryLabMergeRule> rules)
        {
            return RegistryLabMerges.Select(rules, new(new LessonSearchFilter(_course, new FoundGroups()
            {
                Value = [_group],
                IsWildcard = false
            }, GroupPartitionKey.All),
                Builder.LookupModule!.LessonsByCourse, Schedule));
        }
    }
}

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
        var schedule = fixture.Schedule;
        var id = Assert.Single(selection.Lessons);
        var lesson = schedule.Get(id);
        Assert.Equal(expected: DayOfWeek.Monday, actual: lesson.Weekly!.Value.Date.DayOfWeek);
        Assert.Equal(expected: GroupPartitionKey.All, actual: selection.AttendancePartition);
        Assert.Equal(
            expected: 2,
            actual: fixture.Schedule.EnumerateAllLessons()
                .Count());
        Assert.Contains(
            fixture.Schedule.EnumerateAllLessons(),
            item => item.Weekly!.Value.Date.DayOfWeek == DayOfWeek.Wednesday);
    }

    [Fact]
    public void UnconfiguredGroupsDoNotAcquireImplicitMergeBehavior()
    {
        var fixture = new Fixture();
        var noRulesSelection = fixture.Select([]);
        Assert.Empty(noRulesSelection.Lessons);
        var unregisteredCourseSelection = fixture.Select([
            new()
            {
                Course = "Other course",
                Group = "IA2601",
            },
        ]);
        Assert.Empty(unregisteredCourseSelection.Lessons);
        var unregisteredGroupSelection = fixture.Select([
            new()
            {
                Course = "Synthetic course",
                Group = "IA2602",
            },
        ]);
        Assert.Empty(unregisteredGroupSelection.Lessons);
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
        [
            new()
            {
                Course = "Synthetic course",
                Group = "IA2601",
                SourceSubGroup = "III",
            },
        ]));
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
        var filteredSchedule = fixture.Schedule.Filter(new());

        parser.Parse(new(
            new()
            {
                LabMerges = fixture.Rules,
            },
            attendance,
            filteredSchedule,
            workbook));
        var collection = attendance.Build(Attendance.Present);
        IEnumerable<KeyValuePair<
            StudentsLookupKey,
            StudentAttendanceList.BetterAttendances>> collection2 = collection;
        var result = Assert.Single(collection2);
        Assert.Equal(expected: GroupPartitionKey.All, actual: result.Key.GroupPartition);
        Assert.Equal(expected: LessonType.Lab, actual: result.Key.LessonType);
        Assert.Equal(expected: 2, actual: result.Value.StudentNames.Count);
        Assert.Equal(expected: 2, actual: result.Value.Attendance.Values.Count());
        Assert.All(
            result.Value.Attendance.Values,
            day =>
            {
                Assert.All(day.AsArray(), mark => Assert.Equal(expected: Attendance.NotPresent, actual: mark));
            });
    }

    [Fact]
    public void CanonicalSheetsComposeRowsByLessonOrdinalOnlyForConfiguredLabs()
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        var group = fixture.Group;

        var first = builder.List(new StudentsLookupKey(
            [group],
            new GroupPartitionKey(new SubGroup("I"), Specialization.All),
            fixture.Course,
            LessonType.Lab));

        first.Student(Parsing.NameHelper.Parse("Synthetic Alpha"))
            .Day(Attendance.NotPresent);
        first.HintMaxCount(1);

        var second = builder.List(new StudentsLookupKey(
            [group],
            new GroupPartitionKey(new SubGroup("II"), Specialization.All),
            fixture.Course,
            LessonType.Lab));
        var studentAttendanceListBuilder = second;
        var name = Parsing.NameHelper.Parse("Synthetic Beta");
        var student = studentAttendanceListBuilder.Student(name);
        student.Day(Attendance.MotivatedAbsent);
        student.Day(Attendance.NotPresent);
        var canonical = builder.Build(Attendance.Present);
        var unmergedProjection = AttendanceLabMerges.Project(
            canonical,
            fixture.Schedule,
            []);
        Assert.Equal(
            expected: 2,
            actual: unmergedProjection.Count());
        var result = AttendanceLabMerges.Project(
            canonical,
            fixture.Schedule,
            fixture.Rules);
        var merged = Assert.Single(result.Where(entry => entry.Key.GroupPartition == GroupPartitionKey.All));
        Assert.Equal(
            expected: new[]
            {
                Attendance.NotPresent,
                Attendance.MotivatedAbsent,
            },
            actual: merged.Value.Attendance.Day(0)
                .AsArray());
        Assert.Equal(
            expected: new[]
            {
                Attendance.None,
                Attendance.NotPresent,
            },
            actual: merged.Value.Attendance.Day(1)
                .AsArray());
        Assert.Equal(expected: 2, actual: merged.Value.StudentNames.Count);
    }

    [Fact]
    public void DatedCanonicalSheetsPreserveDifferentDatesAndAmbiguousSourceColumns()
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        var group = fixture.Group;

        var first = builder.List(new StudentsLookupKey(
            [group],
            new GroupPartitionKey(new SubGroup("I"), Specialization.All),
            fixture.Course,
            LessonType.Lab));
        first.AddAttendanceDates([
            new()
            {
                Month = 9,
                Day = 1,
            },
            new()
            {
                Month = 9,
                Day = 8,
            },
        ]);
        var studentAttendanceListBuilder = first;
        var name = Parsing.NameHelper.Parse("Synthetic Alpha");
        var alpha = studentAttendanceListBuilder.Student(name);
        alpha.Day(Attendance.NotPresent);
        alpha.Day(Attendance.MotivatedAbsent);

        var second = builder.List(new StudentsLookupKey(
            [group],
            new GroupPartitionKey(new SubGroup("II"), Specialization.All),
            fixture.Course,
            LessonType.Lab));
        second.AddAttendanceDates([
            new()
            {
                Month = 9,
                Day = 2,
            },
            new()
            {
                Month = 9,
                Day = 8,
            },
            new()
            {
                Month = 9,
                Day = 8,
            },
        ]);
        var studentAttendanceListBuilder2 = second;
        var name2 = Parsing.NameHelper.Parse("Synthetic Beta");
        var beta = studentAttendanceListBuilder2.Student(name2);
        beta.Day(Attendance.MotivatedAbsent);
        beta.Day(Attendance.NotPresent);
        beta.Day(Attendance.NotPresent);
        var attendance = builder.Build(Attendance.Present);
        var schedule = fixture.Schedule;
        IReadOnlyList<RegistryLabMergeRule>
            rules = fixture.Rules;
        var result = AttendanceLabMerges.Project(
            attendance,
            schedule,
            rules);
        var merged = Assert.Single(result.Where(entry => entry.Key.GroupPartition == GroupPartitionKey.All));
        Assert.Equal(expected: 3, actual: merged.Value.Dates!.Value.Length);
        Assert.Equal(
            expected: new[]
            {
                Attendance.NotPresent,
                Attendance.None,
            },
            actual: merged.Value.Attendance.Day(0)
                .AsArray());
        Assert.Equal(
            expected: new[]
            {
                Attendance.None,
                Attendance.MotivatedAbsent,
            },
            actual: merged.Value.Attendance.Day(1)
                .AsArray());
        Assert.Equal(
            expected: new[]
            {
                Attendance.MotivatedAbsent,
                Attendance.None,
            },
            actual: merged.Value.Attendance.Day(2)
                .AsArray());
    }

    [Fact]
    public void ExistingWholeGroupSheetTakesPrecedenceOverCanonicalSheets()
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        var group = fixture.Group;

        builder.List(new StudentsLookupKey(
                [group],
                new GroupPartitionKey(new SubGroup("I"), Specialization.All),
                fixture.Course,
                LessonType.Lab))
            .Student(Parsing.NameHelper.Parse("Synthetic Alpha"))
            .Day(Attendance.NotPresent);

        builder.List(new StudentsLookupKey(
                [group],
                GroupPartitionKey.All,
                fixture.Course,
                LessonType.Lab))
            .Student(Parsing.NameHelper.Parse("Synthetic Alpha"))
            .Day(Attendance.MotivatedAbsent);
        var attendance = builder.Build(Attendance.Present);
        var schedule = fixture.Schedule;
        IReadOnlyList<RegistryLabMergeRule>
            rules = fixture.Rules;
        var result = AttendanceLabMerges.Project(
            attendance,
            schedule,
            rules);
        var merged = Assert.Single(result.Where(entry => entry.Key.GroupPartition == GroupPartitionKey.All));
        var firstDayMarks = merged.Value.Attendance.Day(0)
            .AsArray();
        Assert.Equal(
            expected: Attendance.MotivatedAbsent,
            actual: Assert.Single(firstDayMarks));
    }

    [Theory]
    [InlineData(false, typeof(InvalidOperationException))]
    [InlineData(true, typeof(DuplicateAttendanceNameException))]
    public void ConflictingSubgroupSourcesAreRejected(bool duplicateName, Type expectedException)
    {
        var fixture = new Fixture();
        var builder = new AllStudentAttendanceListBuilder();
        var group = fixture.Group;

        var first = builder.List(new StudentsLookupKey(
            [group],
            new GroupPartitionKey(new SubGroup("I"), Specialization.All),
            fixture.Course,
            LessonType.Lab));

        first.Student(Parsing.NameHelper.Parse("Synthetic Alpha"))
            .Day(Attendance.NotPresent);

        var second = builder.List(new StudentsLookupKey(
            [group],
            new GroupPartitionKey(new SubGroup("II"), Specialization.All),
            fixture.Course,
            LessonType.Lab));

        Parsing.Name NameForDuplicateScenario()
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

            var name2 = Parsing.NameHelper.Parse(s);
            return name2;
        }

        second.Student(NameForDuplicateScenario())
            .Day(Attendance.MotivatedAbsent);
        if (!duplicateName)
        {
            second.AddAttendanceDates([
                new()
                {
                    Month = 9,
                    Day = 1,
                },
            ]);
        }

        Assert.Throws(
            expectedException,
            () =>
            {
                var attendance = builder.Build(Attendance.Present);
                var schedule = fixture.Schedule;
                IReadOnlyList<
                        RegistryLabMergeRule>
                    rules = fixture.Rules;
                var project =
                    AttendanceLabMerges.Project(
                        attendance,
                        schedule,
                        rules);
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

        internal readonly RegistryLabMergeRule[] Rules =
        [
            new()
            {
                Course = "Synthetic course",
                Group = "IA2601",
            },
        ];

        internal Fixture(bool includeLecture = false)
        {
            Builder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
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
                var
                    lessonBuilder = lesson;
                SubGroup subGroup = new(subgroup);
                lessonBuilder.SubGroup(subGroup);
                lesson.DayOfWeek(day);
                lesson.TimeSlot(TimeSlot.First);
            });
        }

        internal RegistryLessonSelection Select(IReadOnlyList<RegistryLabMergeRule> rules)
        {
            return RegistryLabMerges.Select(
                rules,
                new(
                    new LessonSearchFilter(
                        _course,
                        new FoundGroups()
                        {
                            Value = [_group],
                            IsWildcard = false,
                        },
                        GroupPartitionKey.All),
                    Builder.LookupModule!.LessonsByCourse,
                    Schedule));
        }
    }
}

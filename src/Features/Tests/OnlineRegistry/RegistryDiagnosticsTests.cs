using Microsoft.Extensions.Logging;
using ScheduleLib.Builders;
using Tests.ScheduleCommon;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistryDiagnosticsTests
{
    [Theory]
    [InlineData(true,
        "ru",
        DayOfWeek.Monday,
        "same",
        true)]
    [InlineData(false,
        "ru",
        DayOfWeek.Monday,
        "same",
        false)]
    [InlineData(true,
        "ro",
        DayOfWeek.Monday,
        "same",
        false)]
    [InlineData(true,
        "ru",
        DayOfWeek.Tuesday,
        "same",
        false)]
    [InlineData(true,
        "ru",
        DayOfWeek.Monday,
        "teacher",
        false)]
    [InlineData(true,
        "ru",
        DayOfWeek.Monday,
        "room",
        false)]
    [InlineData(true,
        "ru",
        DayOfWeek.Monday,
        "lab",
        false)]
    [InlineData(true,
        "ru",
        DayOfWeek.Monday,
        "intake",
        false)]
    [InlineData(true,
        "ru",
        DayOfWeek.Monday,
        "ambiguous",
        false)]
    public void SharedEnglishLectureUsesTheParentLanguageRegistryDestination(
        bool enableRule,
        string subgroup,
        DayOfWeek day,
        string difference,
        bool covered)
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2026),
        });
        var course = builder.Course("Synthetic course");
        var parent = builder.Group("DJ2602(ru)");
        var scheduleBuilder = builder;
        string fullName;
        if (difference == "intake")
        {
            fullName = "DJ2503(en)";
        }
        else
        {
            fullName = "DJ2603(en)";
        }

        var english = scheduleBuilder.Group(fullName);
        if (difference == "ambiguous")
        {
            builder.Group("DJ2604(ru)");
        }

        var teacher = builder.Teacher("Synthetic Teacher");
        var otherTeacher = builder.Teacher("Another Teacher");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Curs);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
            lesson.Teacher(teacher);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(english);
            var lessonBuilder = lesson;
            SubGroup subGroup = new(subgroup);
            lessonBuilder.SubGroup(subGroup);
            var lessonBuilder3 = lesson;
            LessonType type;
            if (difference == "lab")
            {
                type = LessonType.Lab;
            }
            else
            {
                type = LessonType.Curs;
            }

            lessonBuilder3.Type(type);
            lesson.DayOfWeek(day);
            lesson.TimeSlot(TimeSlot.First);
            var lessonBuilder4 = lesson;
            TeacherBuilder teacher2;
            if (difference == "teacher")
            {
                teacher2 = otherTeacher;
            }
            else
            {
                teacher2 = teacher;
            }

            TeacherId teacher3 = teacher2;
            lessonBuilder4.Teacher(teacher3);
            if (difference == "room")
            {
                var lessonBuilder2 = lesson;
                RoomId room = new("222/4");
                lessonBuilder2.Room(room);
            }
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(english);
            var lessonBuilder = lesson;
            string value;
            if (subgroup == "ru")
            {
                value = "ro";
            }
            else
            {
                value = "ru";
            }

            SubGroup subGroup = new(value);
            lessonBuilder.SubGroup(subGroup);
            lesson.Type(LessonType.Curs);
            lesson.DayOfWeek(DayOfWeek.Friday);
            lesson.TimeSlot(TimeSlot.First);
        });
        var diagnostics = new RegistrySyncDiagnostics();
        var subGroup2 = new SubGroup(subgroup);
        var specialization = Specialization.All;
        var partition = new GroupPartitionKey(subGroup2, specialization);
        var schedule = builder.Build();
        var coverage = new RegistryTargetCoverage(
            schedule,
            [
                new(
                    course,
                    parent,
                    GroupPartitionKey.All),
                new(
                    course,
                    english,
                    partition),
            ],
            diagnostics);
        coverage.CoverLesson(
            new WeeklyLessonId(0).AsAny(),
            parent,
            GroupPartitionKey.All,
            enableRule
                ?
                [
                    new()
                    {
                        Faculty = "DJ",
                        SubGroup = "eng",
                        Language = Language.En,
                        MatchByLanguage = true,
                        AllowParentGroup = true,
                    },
                ]
                : []);
        coverage.ReportMissingGroups(course);
        if (covered)
        {
            Assert.Empty(diagnostics.Items);
        }
        else
        {
            var get = schedule.Get(english);
            string expected2 = get.Name;
            var single = Assert.Single(diagnostics.Items);
            string? actual2 = single.Group;
            Assert.Equal(expected: expected2, actual: actual2);
        }
    }

    [Fact]
    public void MissingGroupDiagnosticIdentifiesTheGroupToFix()
    {
        var logger = new CapturingLogger();
        var errors = new RegistryErrorLogger(logger);
        errors.GroupNotFound("IA2502ru(en)");
        Assert.Contains(logger.Messages, message => message.Contains("IA2502ru(en)", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingEnglishDestinationIsReportedWithoutReusingAnotherParent()
    {
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2026);
        var course = builder.Course("Synthetic course");
        var first = builder.Group("IA2501(ru)");
        var second = builder.Group("IA2502");
        var diagnostics = new RegistrySyncDiagnostics();
        var partition = new GroupPartitionKey(SpecialSubGroups.Eng, Specialization.All);
        var coverage = new RegistryTargetCoverage(
            builder.Build(),
            [
                new(
                    course,
                    first,
                    partition),
                new(
                    course,
                    second,
                    partition),
            ],
            diagnostics);
        coverage.ReportMissingCourses(new HashSet<CourseId> { course });
        var registryTargetCoverage = coverage;
        RegistryExpectedTarget target = new(
            course,
            first,
            partition);
        registryTargetCoverage.Cover(target);
        coverage.ReportMissingGroups(course);
        var gap = Assert.Single(diagnostics.Items);
        Assert.Equal(expected: "missing-registry-group", actual: gap.Kind);
        Assert.Equal(expected: "IA2502", actual: gap.Group);
        Assert.Contains(expectedSubstring: "eng", actualString: gap.Detail);
        Assert.Contains(expectedSubstring: "administrator", actualString: gap.Detail);
    }

    [Fact]
    public void MissingCourseAndUnmatchedLabelsAreDeduplicatedAndKeepContext()
    {
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2026);
        var course = builder.Course("Synthetic course");
        var parent = builder.Group("IA2501");
        var diagnostics = new RegistrySyncDiagnostics();
        var coverage = new RegistryTargetCoverage(
            builder.Build(),
            [
                new(
                    course,
                    parent,
                    GroupPartitionKey.All),
            ],
            diagnostics);
        coverage.ReportMissingCourses(new HashSet<CourseId>());
        coverage.ReportMissingGroups(course);
        var errors = new RegistryErrorLogger(new CapturingLogger(), diagnostics);
        errors.GroupNotFound("Synthetic course", "IA2502(en)");
        errors.GroupNotFound("Synthetic course", "IA2502(en)");
        Assert.Equal(expected: 2, actual: diagnostics.Items.Count);
        Assert.Equal(expected: "missing-registry-course", actual: diagnostics.Items[0].Kind);
        Assert.Equal(expected: "Synthetic course", actual: diagnostics.Items[0].Course);
        Assert.Equal(expected: "IA2501", actual: diagnostics.Items[0].Group);
        Assert.Equal(expected: "IA2502(en)", actual: diagnostics.Items[1].Group);
    }

    private sealed class CapturingLogger : ILogger<RegistryErrorLogger>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var list = Messages;
            string item = formatter(state, exception);
            list.Add(item);
        }
    }
}

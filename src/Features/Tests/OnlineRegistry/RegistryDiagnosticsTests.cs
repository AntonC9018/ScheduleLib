using Microsoft.Extensions.Logging;
using ScheduleLib.Builders;
using Tests.ScheduleCommon;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistryDiagnosticsTests
{
    [Theory]
    [InlineData(true, "ru", DayOfWeek.Monday, "same", true)]
    [InlineData(false, "ru", DayOfWeek.Monday, "same", false)]
    [InlineData(true, "ro", DayOfWeek.Monday, "same", false)]
    [InlineData(true, "ru", DayOfWeek.Tuesday, "same", false)]
    [InlineData(true, "ru", DayOfWeek.Monday, "teacher", false)]
    [InlineData(true, "ru", DayOfWeek.Monday, "room", false)]
    [InlineData(true, "ru", DayOfWeek.Monday, "lab", false)]
    [InlineData(true, "ru", DayOfWeek.Monday, "intake", false)]
    [InlineData(true, "ru", DayOfWeek.Monday, "ambiguous", false)]
    public void SharedEnglishLectureUsesTheParentLanguageRegistryDestination(
        bool enableRule, string subgroup, DayOfWeek day, string difference, bool covered)
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var course = builder.Course("Synthetic course");
        var parent = builder.Group("DJ2602(ru)");
        var english = builder.Group(difference == "intake" ? "DJ2503(en)" : "DJ2603(en)");
        if (difference == "ambiguous") builder.Group("DJ2604(ru)");
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
            lesson.SubGroup(new(subgroup));
            lesson.Type(difference == "lab" ? LessonType.Lab : LessonType.Curs);
            lesson.DayOfWeek(day);
            lesson.TimeSlot(TimeSlot.First);
            lesson.Teacher(difference == "teacher" ? otherTeacher : teacher);
            if (difference == "room") lesson.Room(new("222/4"));
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(english);
            lesson.SubGroup(new(subgroup == "ru" ? "ro" : "ru"));
            lesson.Type(LessonType.Curs);
            lesson.DayOfWeek(DayOfWeek.Friday);
            lesson.TimeSlot(TimeSlot.First);
        });
        var diagnostics = new RegistrySyncDiagnostics();
        var partition = new GroupPartitionKey(new SubGroup(subgroup), Specialization.All);
        var schedule = builder.Build();
        var coverage = new RegistryTargetCoverage(schedule,
            [new(course, parent, GroupPartitionKey.All), new(course, english, partition)], diagnostics);
        coverage.CoverLesson(new WeeklyLessonId(0).AsAny(), parent, GroupPartitionKey.All,
            enableRule ? [new() { Faculty = "DJ", SubGroup = "eng", Language = Language.En,
                MatchByLanguage = true, AllowParentGroup = true }] : []);
        coverage.ReportMissingGroups(course);
        if (covered) Assert.Empty(diagnostics.Items);
        else Assert.Equal(schedule.Get(english).Name, Assert.Single(diagnostics.Items).Group);
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
        var coverage = new RegistryTargetCoverage(builder.Build(),
            [new(course, first, partition), new(course, second, partition)], diagnostics);
        coverage.ReportMissingCourses(new HashSet<CourseId> { course });
        coverage.Cover(new(course, first, partition));
        coverage.ReportMissingGroups(course);
        var gap = Assert.Single(diagnostics.Items);
        Assert.Equal("missing-registry-group", gap.Kind);
        Assert.Equal("IA2502", gap.Group);
        Assert.Contains("eng", gap.Detail);
        Assert.Contains("administrator", gap.Detail);
    }

    [Fact]
    public void MissingCourseAndUnmatchedLabelsAreDeduplicatedAndKeepContext()
    {
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2026);
        var course = builder.Course("Synthetic course");
        var parent = builder.Group("IA2501");
        var diagnostics = new RegistrySyncDiagnostics();
        var coverage = new RegistryTargetCoverage(builder.Build(), [new(course, parent, GroupPartitionKey.All)], diagnostics);
        coverage.ReportMissingCourses(new HashSet<CourseId>());
        coverage.ReportMissingGroups(course);
        var errors = new RegistryErrorLogger(new CapturingLogger(), diagnostics);
        errors.GroupNotFound("Synthetic course", "IA2502(en)");
        errors.GroupNotFound("Synthetic course", "IA2502(en)");
        Assert.Equal(2, diagnostics.Items.Count);
        Assert.Equal("missing-registry-course", diagnostics.Items[0].Kind);
        Assert.Equal("Synthetic course", diagnostics.Items[0].Course);
        Assert.Equal("IA2501", diagnostics.Items[0].Group);
        Assert.Equal("IA2502(en)", diagnostics.Items[1].Group);
    }

    private sealed class CapturingLogger : ILogger<RegistryErrorLogger>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}

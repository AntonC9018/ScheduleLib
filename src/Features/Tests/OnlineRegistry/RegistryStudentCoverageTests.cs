using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;
using Xunit;

public sealed class RegistryStudentCoverageTests
{
    [Fact]
    public void SplitRostersAreMatchedTogetherAndBothMismatchDirectionsAreReportedOnce()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((key, ["Synthetic Ana", "Synthetic Boris", "Synthetic Carla", "Synthetic Elena"])), null);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris", "Synthetic Dina"), [key], [key]);
        coverage.Observe(Roster("Synthetic Carla", "Synthetic Dina"), [key], [key]);
        var diagnostics = fixture.Report(coverage);
        Assert.StartsWith("1 attendance students", Assert.Single(diagnostics.Items, item => item.Kind == "attendance-students-not-in-registry").Detail);
        Assert.StartsWith("1 registry students", Assert.Single(diagnostics.Items, item => item.Kind == "unmatched-attendance-students").Detail);
        Assert.All(diagnostics.Items, item => Assert.DoesNotContain("Synthetic", item.Detail));
    }

    [Fact]
    public void OverlappingRostersAndAliasesUseTheSameMatchingAsAttendanceWrites()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((key, ["Synthetic Ana", "Synthetic Boris"])),
            new Dictionary<string, string> { ["Synthetic Anna"] = "Synthetic Ana" });
        coverage.Observe(Roster("Synthetic Anna"), [key], [key]);
        coverage.Observe(Roster("Synthetic Anna", "Synthetic Boris"), [key], [key]);
        Assert.Empty(fixture.Report(coverage).Items);
    }

    [Fact]
    public void UnmatchedAliasesInBothRostersCountAsOneStudent()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((key, ["Synthetic Boris"])),
            new Dictionary<string, string> { ["Synthetic Anna"] = "Synthetic Ana" });
        coverage.Observe(Roster("Synthetic Anna", "Synthetic Boris"), [key], [key]);
        coverage.Observe(Roster("Synthetic Ana"), [key], [key]);
        var missing = Assert.Single(fixture.Report(coverage).Items);
        Assert.Equal("unmatched-attendance-students", missing.Kind);
        Assert.StartsWith("1 registry students", missing.Detail);
    }

    [Fact]
    public void AnUnrelatedAudienceCannotHideMissingStudents()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var second = fixture.Key(fixture.Second);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Boris"])), null);
        coverage.Observe(Roster("Synthetic Boris"), [first], [first]);
        coverage.Observe(Roster("Synthetic Boris"), [second], [second]);
        var diagnostics = fixture.Report(coverage);
        Assert.Equal(2, diagnostics.Items.Count);
        Assert.All(diagnostics.Items, item => Assert.Equal("IA2601", item.Group));
    }

    [Fact]
    public void CombinedMembershipDoesNotWarnAboutOtherParentsIntentionallyLeftBlank()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var second = fixture.Key(fixture.Second);
        var combined = first.WithGroups([fixture.First, fixture.Second]);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Boris"])), null);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), [second], [combined]);
        coverage.Observe(Roster("Synthetic Ana"), [first], [first]);
        Assert.Empty(fixture.Report(coverage).Items);
    }

    [Fact]
    public void ACombinedRosterCannotValidateAnUnrelatedParentOnlyRosterTransitively()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var second = fixture.Key(fixture.Second);
        var combined = first.WithGroups([fixture.First, fixture.Second]);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Boris"])), null);
        coverage.Observe(Roster("Synthetic Boris"), [first], [first]);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), [combined], [combined]);
        var missing = Assert.Single(fixture.Report(coverage).Items);
        Assert.Equal("unmatched-attendance-students", missing.Kind);
        Assert.StartsWith("1 registry students", missing.Detail);
    }

    [Fact]
    public void MatchingAnotherLessonTypeDoesNotHideMissingLabEnrollment()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with { LessonType = LessonType.Curs };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((lab, ["Synthetic Ana"]), (lecture, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Dina"), [lab], [lab]);
        coverage.Observe(Roster("Synthetic Ana"), [lecture], [lecture]);
        var missing = Assert.Single(fixture.Report(coverage).Items, item => item.Kind == "attendance-students-not-in-registry");
        Assert.Contains("Lab", missing.Detail);
    }

    [Fact]
    public void SeparateLessonTypesKeepTheirSourceAndEnrollmentDiagnostics()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with { LessonType = LessonType.Curs };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((lab, ["Synthetic Boris"]), (lecture, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), [lab], [lab]);
        coverage.Observe(Roster("Synthetic Boris"), [lecture], [lecture]);
        var diagnostics = fixture.Report(coverage).Items;
        Assert.Contains("Curs", Assert.Single(diagnostics, item => item.Kind == "attendance-students-not-in-registry").Detail);
        var unknown = diagnostics.Where(item => item.Kind == "unmatched-attendance-students").ToArray();
        Assert.Equal(2, unknown.Length);
        Assert.Contains(unknown, item => item.Detail.Contains("for Curs", StringComparison.Ordinal));
        Assert.Contains(unknown, item => item.Detail.Contains("for Lab", StringComparison.Ordinal));
    }

    [Fact]
    public void MembershipOnlySourcesRemainKnownWhenOnlyAnotherLessonTypeIsRouted()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with { LessonType = LessonType.Curs };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((lab, ["Synthetic Boris"]), (lecture, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Boris"), [lecture], [lab, lecture]);
        Assert.Equal("attendance-students-not-in-registry", Assert.Single(fixture.Report(coverage).Items).Kind);
    }

    [Fact]
    public void EqualUnknownCountsForDifferentSubgroupsProduceSeparateDiagnostics()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First) with { GroupPartition = new(SubGroup.CreateNumeric(1), Specialization.All) };
        var second = first with { GroupPartition = new(SubGroup.CreateNumeric(2), Specialization.All) };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Boris"])), null);
        coverage.Observe(Roster("Synthetic Dina"), [first], [first]);
        coverage.Observe(Roster("Synthetic Elena"), [second], [second]);
        var unknown = fixture.Report(coverage).Items.Where(item => item.Kind == "unmatched-attendance-students").ToArray();
        Assert.Equal(2, unknown.Length);
        Assert.Contains(unknown, item => item.Detail.Contains("(subgroup I)", StringComparison.Ordinal));
        Assert.Contains(unknown, item => item.Detail.Contains("(subgroup II)", StringComparison.Ordinal));
    }

    [Fact]
    public void AnotherSubgroupCannotHideMissingEnrollment()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First) with { GroupPartition = new(SubGroup.CreateNumeric(1), Specialization.All) };
        var second = first with { GroupPartition = new(SubGroup.CreateNumeric(2), Specialization.All) };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Dina"), [first], [first]);
        coverage.Observe(Roster("Synthetic Ana"), [second], [second]);
        var missing = Assert.Single(fixture.Report(coverage).Items, item => item.Kind == "attendance-students-not-in-registry");
        Assert.Contains("subgroup I", missing.Detail);
    }

    [Fact]
    public void MissingMembershipSourcesDeferRegistryMismatchAndExpelledNamesAreIgnored()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var combined = first.WithGroups([fixture.First, fixture.Second]);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), [first], [combined]);
        Assert.Empty(fixture.Report(coverage).Items);
        var expelled = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"])), null);
        expelled.Observe([new("Synthetic Ana", false), new("Synthetic Dina", true)], [first], [first]);
        Assert.Empty(fixture.Report(expelled).Items);
    }

    [Fact]
    public void EmptyRosterReportsKnownSourceStudentsMissing()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((key, ["Synthetic Ana"])), null);
        coverage.Observe([], [key], [key]);
        Assert.Equal("attendance-students-not-in-registry", Assert.Single(fixture.Report(coverage).Items).Kind);
    }

    private static HtmlStudent[] Roster(params string[] names) => names.Select(name => new HtmlStudent(name, false)).ToArray();

    private sealed class Fixture
    {
        internal Schedule Schedule { get; }
        internal CourseId Course { get; }
        internal GroupId First { get; }
        internal GroupId Second { get; }

        internal Fixture()
        {
            var builder = new ScheduleBuilder();
            builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
            builder.EnableLookupModule();
            Course = builder.Course("Coverage course");
            First = builder.Group("IA2601");
            Second = builder.Group("IA2602");
            Schedule = builder.Build();
        }

        internal StudentsLookupKey Key(GroupId group) => new([group], GroupPartitionKey.All, Course, LessonType.Lab);

        internal StudentAttendanceList Attendance(params (StudentsLookupKey Key, string[] Names)[] sources)
        {
            var builder = new AllStudentAttendanceListBuilder();
            foreach (var source in sources)
            {
                var list = builder.List(source.Key);
                foreach (var name in source.Names) list.Student(NameHelper.Parse(name)).Day(ScheduleLib.OnlineRegistry.Attendance.NotPresent);
            }
            return builder.Build();
        }

        internal RegistrySyncDiagnostics Report(RegistryStudentCoverage coverage)
        {
            var diagnostics = new RegistrySyncDiagnostics();
            coverage.Report(Schedule, Course, diagnostics);
            return diagnostics;
        }
    }
}

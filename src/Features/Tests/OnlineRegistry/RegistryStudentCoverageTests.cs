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
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris", "Synthetic Dina"), routedKeys: [key], membershipKeys: [key]);
        coverage.Observe(Roster("Synthetic Carla", "Synthetic Dina"), routedKeys: [key], membershipKeys: [key]);
        var diagnostics = fixture.Report(coverage);
        Assert.StartsWith(expectedStartString: "1 attendance students", actualString: Assert.Single(diagnostics.Items, item => item.Kind == "attendance-students-not-in-registry").Detail);
        Assert.StartsWith(expectedStartString: "1 registry students", actualString: Assert.Single(diagnostics.Items, item => item.Kind == "unmatched-attendance-students").Detail);
        Assert.All(diagnostics.Items, item => Assert.DoesNotContain(expectedSubstring: "Synthetic", actualString: item.Detail));
    }

    [Fact]
    public void OverlappingRostersAndAliasesUseTheSameMatchingAsAttendanceWrites()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((key, ["Synthetic Ana", "Synthetic Boris"])),
            new Dictionary<string, string> { ["Synthetic Anna"] = "Synthetic Ana" });
        coverage.Observe(Roster("Synthetic Anna"), routedKeys: [key], membershipKeys: [key]);
        coverage.Observe(Roster("Synthetic Anna", "Synthetic Boris"), routedKeys: [key], membershipKeys: [key]);
        Assert.Empty(fixture.Report(coverage).Items);
    }

    [Fact]
    public void UnmatchedAliasesInBothRostersCountAsOneStudent()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((key, ["Synthetic Boris"])),
            new Dictionary<string, string> { ["Synthetic Anna"] = "Synthetic Ana" });
        coverage.Observe(Roster("Synthetic Anna", "Synthetic Boris"), routedKeys: [key], membershipKeys: [key]);
        coverage.Observe(Roster("Synthetic Ana"), routedKeys: [key], membershipKeys: [key]);
        var missing = Assert.Single(fixture.Report(coverage).Items);
        Assert.Equal(expected: "unmatched-attendance-students", actual: missing.Kind);
        Assert.StartsWith(expectedStartString: "1 registry students", actualString: missing.Detail);
    }

    [Fact]
    public void AnUnrelatedAudienceCannotHideMissingStudents()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var second = fixture.Key(fixture.Second);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Boris"])), null);
        coverage.Observe(Roster("Synthetic Boris"), routedKeys: [first], membershipKeys: [first]);
        coverage.Observe(Roster("Synthetic Boris"), routedKeys: [second], membershipKeys: [second]);
        var diagnostics = fixture.Report(coverage);
        Assert.Equal(expected: 2, actual: diagnostics.Items.Count);
        Assert.All(diagnostics.Items, item => Assert.Equal(expected: "IA2601", actual: item.Group));
    }

    [Fact]
    public void CombinedMembershipDoesNotWarnAboutOtherParentsIntentionallyLeftBlank()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var second = fixture.Key(fixture.Second);
        var combined = first.WithGroups([fixture.First, fixture.Second]);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Boris"])), null);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), routedKeys: [second], membershipKeys: [combined]);
        coverage.Observe(Roster("Synthetic Ana"), routedKeys: [first], membershipKeys: [first]);
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
        coverage.Observe(Roster("Synthetic Boris"), routedKeys: [first], membershipKeys: [first]);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), routedKeys: [combined], membershipKeys: [combined]);
        var missing = Assert.Single(fixture.Report(coverage).Items);
        Assert.Equal(expected: "unmatched-attendance-students", actual: missing.Kind);
        Assert.StartsWith(expectedStartString: "1 registry students", actualString: missing.Detail);
    }

    [Fact]
    public void MatchingAnotherLessonTypeDoesNotHideMissingLabEnrollment()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with { LessonType = LessonType.Curs };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((lab, ["Synthetic Ana"]), (lecture, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Dina"), routedKeys: [lab], membershipKeys: [lab]);
        coverage.Observe(Roster("Synthetic Ana"), routedKeys: [lecture], membershipKeys: [lecture]);
        var missing = Assert.Single(fixture.Report(coverage).Items, item => item.Kind == "attendance-students-not-in-registry");
        Assert.Contains(expectedSubstring: "Lab", actualString: missing.Detail);
    }

    [Fact]
    public void SeparateLessonTypesKeepTheirSourceAndEnrollmentDiagnostics()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with { LessonType = LessonType.Curs };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((lab, ["Synthetic Boris"]), (lecture, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), routedKeys: [lab], membershipKeys: [lab]);
        coverage.Observe(Roster("Synthetic Boris"), routedKeys: [lecture], membershipKeys: [lecture]);
        var diagnostics = fixture.Report(coverage).Items;
        Assert.Contains(expectedSubstring: "Curs", actualString: Assert.Single(diagnostics, item => item.Kind == "attendance-students-not-in-registry").Detail);
        var unknown = diagnostics.Where(item => item.Kind == "unmatched-attendance-students").ToArray();
        Assert.Equal(expected: 2, actual: unknown.Length);
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
        coverage.Observe(Roster("Synthetic Boris"), routedKeys: [lecture], membershipKeys: [lab, lecture]);
        Assert.Equal(expected: "attendance-students-not-in-registry", actual: Assert.Single(fixture.Report(coverage).Items).Kind);
    }

    [Fact]
    public void EqualUnknownCountsForDifferentSubgroupsProduceSeparateDiagnostics()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First) with { GroupPartition = new(SubGroup.CreateNumeric(1), Specialization.All) };
        var second = first with { GroupPartition = new(SubGroup.CreateNumeric(2), Specialization.All) };
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"]), (second, ["Synthetic Boris"])), null);
        coverage.Observe(Roster("Synthetic Dina"), routedKeys: [first], membershipKeys: [first]);
        coverage.Observe(Roster("Synthetic Elena"), routedKeys: [second], membershipKeys: [second]);
        var unknown = fixture.Report(coverage).Items.Where(item => item.Kind == "unmatched-attendance-students").ToArray();
        Assert.Equal(expected: 2, actual: unknown.Length);
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
        coverage.Observe(Roster("Synthetic Dina"), routedKeys: [first], membershipKeys: [first]);
        coverage.Observe(Roster("Synthetic Ana"), routedKeys: [second], membershipKeys: [second]);
        var missing = Assert.Single(fixture.Report(coverage).Items, item => item.Kind == "attendance-students-not-in-registry");
        Assert.Contains(expectedSubstring: "subgroup I", actualString: missing.Detail);
    }

    [Fact]
    public void MissingMembershipSourcesDeferRegistryMismatchAndExpelledNamesAreIgnored()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var combined = first.WithGroups([fixture.First, fixture.Second]);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"])), null);
        coverage.Observe(Roster("Synthetic Ana", "Synthetic Boris"), routedKeys: [first], membershipKeys: [combined]);
        Assert.Empty(fixture.Report(coverage).Items);
        var expelled = new RegistryStudentCoverage(fixture.Attendance((first, ["Synthetic Ana"])), null);
        expelled.Observe([new("Synthetic Ana", false), new("Synthetic Dina", true)], routedKeys: [first], membershipKeys: [first]);
        Assert.Empty(fixture.Report(expelled).Items);
    }

    [Fact]
    public void EmptyRosterReportsKnownSourceStudentsMissing()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var coverage = new RegistryStudentCoverage(fixture.Attendance((key, ["Synthetic Ana"])), null);
        coverage.Observe([], routedKeys: [key], membershipKeys: [key]);
        Assert.Equal(expected: "attendance-students-not-in-registry", actual: Assert.Single(fixture.Report(coverage).Items).Kind);
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

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
        var attendance = fixture.Attendance(new Fixture.AttendanceSource(key,
            ["Synthetic Ana", "Synthetic Boris", "Synthetic Carla", "Synthetic Elena"]));
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Ana",
            "Synthetic Boris",
            "Synthetic Dina");
        coverage.Observe(
            students,
            routedKeys: [key],
            membershipKeys: [key]);
        var students2 = Roster("Synthetic Carla", "Synthetic Dina");
        coverage.Observe(
            students2,
            routedKeys: [key],
            membershipKeys: [key]);
        var diagnostics = fixture.Report(coverage);
        string expectedStartString2 = "1 attendance students";
        var single = Assert.Single(diagnostics.Items, item => item.Kind == "attendance-students-not-in-registry");
        string actualString2 = single.Detail;
        Assert.StartsWith(expectedStartString: expectedStartString2, actualString: actualString2);
        string expectedStartString3 = "1 registry students";
        var single2 = Assert.Single(diagnostics.Items, item => item.Kind == "unmatched-attendance-students");
        string actualString3 = single2.Detail;
        Assert.StartsWith(expectedStartString: expectedStartString3, actualString: actualString3);
        Assert.All(diagnostics.Items,
            item => Assert.DoesNotContain(expectedSubstring: "Synthetic", actualString: item.Detail));
    }

    [Fact]
    public void OverlappingRostersAndAliasesUseTheSameMatchingAsAttendanceWrites()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);

        StudentAttendanceList CalculateAttendance()
        {
            var attendance =
                fixture.Attendance(new Fixture.AttendanceSource(key, ["Synthetic Ana", "Synthetic Boris"]));
            return attendance;
        }

        var coverage = new RegistryStudentCoverage(CalculateAttendance(),
            new Dictionary<string, string>
            {
                ["Synthetic Anna"] = "Synthetic Ana",
            });
        var students = Roster("Synthetic Anna");
        coverage.Observe(
            students,
            routedKeys: [key],
            membershipKeys: [key]);
        var students2 = Roster("Synthetic Anna", "Synthetic Boris");
        coverage.Observe(
            students2,
            routedKeys: [key],
            membershipKeys: [key]);
        var report = fixture.Report(coverage);
        System.Collections.IEnumerable collection = report.Items;
        Assert.Empty(collection);
    }

    [Fact]
    public void UnmatchedAliasesInBothRostersCountAsOneStudent()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);

        StudentAttendanceList CalculateAttendance()
        {
            var attendance = fixture.Attendance(new Fixture.AttendanceSource(key, ["Synthetic Boris"]));
            return attendance;
        }

        var coverage = new RegistryStudentCoverage(CalculateAttendance(),
            new Dictionary<string, string>
            {
                ["Synthetic Anna"] = "Synthetic Ana",
            });
        var students = Roster("Synthetic Anna", "Synthetic Boris");
        coverage.Observe(
            students,
            routedKeys: [key],
            membershipKeys: [key]);
        var students2 = Roster("Synthetic Ana");
        coverage.Observe(
            students2,
            routedKeys: [key],
            membershipKeys: [key]);
        var report = fixture.Report(coverage);
        IEnumerable<RegistrySyncDiagnostic> collection = report.Items;
        var missing = Assert.Single(collection);
        Assert.Equal(expected: "unmatched-attendance-students", actual: missing.Kind);
        Assert.StartsWith(expectedStartString: "1 registry students", actualString: missing.Detail);
    }

    [Fact]
    public void AnUnrelatedAudienceCannotHideMissingStudents()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var second = fixture.Key(fixture.Second);
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(first, ["Synthetic Ana"]),
            new(second, ["Synthetic Boris"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Boris");
        coverage.Observe(
            students,
            routedKeys: [first],
            membershipKeys: [first]);
        var students2 = Roster("Synthetic Boris");
        coverage.Observe(
            students2,
            routedKeys: [second],
            membershipKeys: [second]);
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
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(first, ["Synthetic Ana"]),
            new(second, ["Synthetic Boris"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Ana", "Synthetic Boris");
        coverage.Observe(
            students,
            routedKeys: [second],
            membershipKeys: [combined]);
        var students2 = Roster("Synthetic Ana");
        coverage.Observe(
            students2,
            routedKeys: [first],
            membershipKeys: [first]);
        var report = fixture.Report(coverage);
        System.Collections.IEnumerable collection = report.Items;
        Assert.Empty(collection);
    }

    [Fact]
    public void ACombinedRosterCannotValidateAnUnrelatedParentOnlyRosterTransitively()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var second = fixture.Key(fixture.Second);
        var combined = first.WithGroups([fixture.First, fixture.Second]);
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(first, ["Synthetic Ana"]),
            new(second, ["Synthetic Boris"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Boris");
        coverage.Observe(
            students,
            routedKeys: [first],
            membershipKeys: [first]);
        var students2 = Roster("Synthetic Ana", "Synthetic Boris");
        coverage.Observe(
            students2,
            routedKeys: [combined],
            membershipKeys: [combined]);
        var report = fixture.Report(coverage);
        IEnumerable<RegistrySyncDiagnostic> collection = report.Items;
        var missing = Assert.Single(collection);
        Assert.Equal(expected: "unmatched-attendance-students", actual: missing.Kind);
        Assert.StartsWith(expectedStartString: "1 registry students", actualString: missing.Detail);
    }

    [Fact]
    public void MatchingAnotherLessonTypeDoesNotHideMissingLabEnrollment()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with
        {
            LessonType = LessonType.Curs,
        };
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(lab, ["Synthetic Ana"]),
            new(lecture, ["Synthetic Ana"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Dina");
        coverage.Observe(
            students,
            routedKeys: [lab],
            membershipKeys: [lab]);
        var students2 = Roster("Synthetic Ana");
        coverage.Observe(
            students2,
            routedKeys: [lecture],
            membershipKeys: [lecture]);
        var report = fixture.Report(coverage);
        IEnumerable<RegistrySyncDiagnostic> collection = report.Items;
        Predicate<RegistrySyncDiagnostic> predicate = item => item.Kind == "attendance-students-not-in-registry";
        var missing = Assert.Single(collection, predicate);
        Assert.Contains(expectedSubstring: "Lab", actualString: missing.Detail);
    }

    [Fact]
    public void SeparateLessonTypesKeepTheirSourceAndEnrollmentDiagnostics()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with
        {
            LessonType = LessonType.Curs,
        };
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(lab, ["Synthetic Boris"]),
            new(lecture, ["Synthetic Ana"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Ana", "Synthetic Boris");
        coverage.Observe(
            students,
            routedKeys: [lab],
            membershipKeys: [lab]);
        var students2 = Roster("Synthetic Boris");
        coverage.Observe(
            students2,
            routedKeys: [lecture],
            membershipKeys: [lecture]);
        var diagnostics = fixture.Report(coverage).Items;
        string expectedSubstring2 = "Curs";
        var single = Assert.Single(diagnostics, item => item.Kind == "attendance-students-not-in-registry");
        string actualString2 = single.Detail;
        Assert.Contains(expectedSubstring: expectedSubstring2, actualString: actualString2);
        var unknown = diagnostics.Where(item => item.Kind == "unmatched-attendance-students")
            .ToArray();
        Assert.Equal(expected: 2, actual: unknown.Length);
        Assert.Contains(unknown, item => item.Detail.Contains("for Curs", StringComparison.Ordinal));
        Assert.Contains(unknown, item => item.Detail.Contains("for Lab", StringComparison.Ordinal));
    }

    [Fact]
    public void MembershipOnlySourcesRemainKnownWhenOnlyAnotherLessonTypeIsRouted()
    {
        var fixture = new Fixture();
        var lab = fixture.Key(fixture.First);
        var lecture = lab with
        {
            LessonType = LessonType.Curs,
        };
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(lab, ["Synthetic Boris"]),
            new(lecture, ["Synthetic Ana"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Boris");
        coverage.Observe(
            students,
            routedKeys: [lecture],
            membershipKeys: [lab, lecture]);
        string expected2 = "attendance-students-not-in-registry";
        var report = fixture.Report(coverage);
        IEnumerable<RegistrySyncDiagnostic> collection = report.Items;
        var single = Assert.Single(collection);
        string actual2 = single.Kind;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void EqualUnknownCountsForDifferentSubgroupsProduceSeparateDiagnostics()
    {
        var fixture = new Fixture();
        var firstSubGroup = SubGroup.CreateNumeric(1);
        var first = fixture.Key(fixture.First) with
        {
            GroupPartition = new(firstSubGroup, Specialization.All),
        };
        var secondSubGroup = SubGroup.CreateNumeric(2);
        var second = first with
        {
            GroupPartition = new(secondSubGroup, Specialization.All),
        };
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(first, ["Synthetic Ana"]),
            new(second, ["Synthetic Boris"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Dina");
        coverage.Observe(
            students,
            routedKeys: [first],
            membershipKeys: [first]);
        var students2 = Roster("Synthetic Elena");
        coverage.Observe(
            students2,
            routedKeys: [second],
            membershipKeys: [second]);
        var unknown = fixture.Report(coverage).Items
            .Where(item => item.Kind == "unmatched-attendance-students")
            .ToArray();
        Assert.Equal(expected: 2, actual: unknown.Length);
        Assert.Contains(unknown, item => item.Detail.Contains("(subgroup I)", StringComparison.Ordinal));
        Assert.Contains(unknown, item => item.Detail.Contains("(subgroup II)", StringComparison.Ordinal));
    }

    [Fact]
    public void AnotherSubgroupCannotHideMissingEnrollment()
    {
        var fixture = new Fixture();
        var firstSubGroup = SubGroup.CreateNumeric(1);
        var first = fixture.Key(fixture.First) with
        {
            GroupPartition = new(firstSubGroup, Specialization.All),
        };
        var secondSubGroup = SubGroup.CreateNumeric(2);
        var second = first with
        {
            GroupPartition = new(secondSubGroup, Specialization.All),
        };
        Fixture.AttendanceSource[] attendanceSources =
        [
            new(first, ["Synthetic Ana"]),
            new(second, ["Synthetic Ana"]),
        ];
        var attendance = fixture.Attendance(attendanceSources);
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Dina");
        coverage.Observe(
            students,
            routedKeys: [first],
            membershipKeys: [first]);
        var students2 = Roster("Synthetic Ana");
        coverage.Observe(
            students2,
            routedKeys: [second],
            membershipKeys: [second]);
        var report = fixture.Report(coverage);
        IEnumerable<RegistrySyncDiagnostic> collection = report.Items;
        Predicate<RegistrySyncDiagnostic> predicate = item => item.Kind == "attendance-students-not-in-registry";
        var missing = Assert.Single(collection, predicate);
        Assert.Contains(expectedSubstring: "subgroup I", actualString: missing.Detail);
    }

    [Fact]
    public void MissingMembershipSourcesDeferRegistryMismatchAndExpelledNamesAreIgnored()
    {
        var fixture = new Fixture();
        var first = fixture.Key(fixture.First);
        var combined = first.WithGroups([fixture.First, fixture.Second]);
        var attendance = fixture.Attendance(new Fixture.AttendanceSource(first, ["Synthetic Ana"]));
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        var students = Roster("Synthetic Ana", "Synthetic Boris");
        coverage.Observe(
            students,
            routedKeys: [first],
            membershipKeys: [combined]);
        var report = fixture.Report(coverage);
        System.Collections.IEnumerable collection = report.Items;
        Assert.Empty(collection);
        var attendance2 = fixture.Attendance(new Fixture.AttendanceSource(first, ["Synthetic Ana"]));
        IReadOnlyDictionary<string, string>? aliases2 = null;
        var expelled = new RegistryStudentCoverage(attendance2, aliases2);
        expelled.Observe([new("Synthetic Ana", false), new("Synthetic Dina", true)],
            routedKeys: [first],
            membershipKeys: [first]);
        var report2 = fixture.Report(expelled);
        System.Collections.IEnumerable collection2 = report2.Items;
        Assert.Empty(collection2);
    }

    [Fact]
    public void EmptyRosterReportsKnownSourceStudentsMissing()
    {
        var fixture = new Fixture();
        var key = fixture.Key(fixture.First);
        var attendance = fixture.Attendance(new Fixture.AttendanceSource(key, ["Synthetic Ana"]));
        IReadOnlyDictionary<string, string>? aliases = null;
        var coverage = new RegistryStudentCoverage(attendance, aliases);
        coverage.Observe([],
            routedKeys: [key],
            membershipKeys: [key]);
        string expected2 = "attendance-students-not-in-registry";
        var report = fixture.Report(coverage);
        IEnumerable<RegistrySyncDiagnostic> collection = report.Items;
        var single = Assert.Single(collection);
        string actual2 = single.Kind;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    private static HtmlStudent[] Roster(params string[] names)
    {
        return names.Select(name => new HtmlStudent(name, false))
            .ToArray();
    }

    private sealed class Fixture
    {
        /// <summary>One attendance list passed to the coverage fixture, with its student names.</summary>
        internal readonly record struct AttendanceSource(StudentsLookupKey Key, string[] Names);

        internal Schedule Schedule { get; }
        internal CourseId Course { get; }
        internal GroupId First { get; }
        internal GroupId Second { get; }

        internal Fixture()
        {
            var builder = new ScheduleBuilder();
            builder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
            builder.EnableLookupModule();
            Course = builder.Course("Coverage course");
            First = builder.Group("IA2601");
            Second = builder.Group("IA2602");
            Schedule = builder.Build();
        }

        internal StudentsLookupKey Key(GroupId group)
        {
            return new([group],
                GroupPartitionKey.All,
                Course,
                LessonType.Lab);
        }

        internal StudentAttendanceList Attendance(params AttendanceSource[] sources)
        {
            var builder = new AllStudentAttendanceListBuilder();
            foreach (var source in sources)
            {
                var list = builder.List(source.Key);
                foreach (var name in source.Names)
                {
                    list.Student(NameHelper.Parse(name))
                        .Day(ScheduleLib.OnlineRegistry.Attendance.NotPresent);
                }
            }
            return builder.Build();
        }

        internal RegistrySyncDiagnostics Report(RegistryStudentCoverage coverage)
        {
            var diagnostics = new RegistrySyncDiagnostics();
            coverage.Report(Schedule,
                Course,
                diagnostics);
            return diagnostics;
        }
    }
}

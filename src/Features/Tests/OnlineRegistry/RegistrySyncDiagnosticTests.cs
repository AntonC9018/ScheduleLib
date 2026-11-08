using System.Text.Json;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistrySyncDiagnosticTests
{
    public static TheoryData<Action<RegistrySyncDiagnostics>, RegistrySyncDiagnosticKind, string> Reports
    {
        get
        {
            return new()
            {
                {
                    diagnostics => diagnostics.ReportRegistryDeletionRefused(
                        course: "Synthetic course",
                        @group: "IA2501",
                        new DateTime(
                            year: 2026,
                            month: 11,
                            day: 2,
                            hour: 9,
                            minute: 0,
                            second: 0)),
                    RegistrySyncDiagnosticKind.RegistryDeletionRefused,
                    "Skipped deletion on 2026-11-02 09:00: the lesson remains unchanged. No retry was performed."
                },
                {
                    diagnostics => diagnostics.ReportRegistryPartitionMismatch(
                        course: "Synthetic course",
                        @group: "IA2501",
                        GroupPartitionKey.All),
                    RegistrySyncDiagnosticKind.RegistryPartitionMismatch,
                    "No schedule lessons match whole group. Check the registry assignment and schedule partition mapping."
                },
                {
                    diagnostics => diagnostics.ReportMissingAttendanceSheet(
                        course: "Synthetic course",
                        @group: "IA2501",
                        LessonType.Lab,
                        GroupPartitionKey.All),
                    RegistrySyncDiagnosticKind.MissingAttendanceSheet,
                    "No attendance rows were loaded for Lab (whole group). Configure or correct the attendance worksheet for this course/group partition."
                },
                {
                    diagnostics => diagnostics.ReportAmbiguousRegistryAssessment(
                        course: "Synthetic course",
                        @group: "IA2501",
                        new DateTime(
                            year: 2026,
                            month: 11,
                            day: 2,
                            hour: 9,
                            minute: 0,
                            second: 0)),
                    RegistrySyncDiagnosticKind.AmbiguousRegistryAssessment,
                    "Assessment on 2026-11-02 09:00 has no scope identifying its scheduled audience. Its entry and candidate sessions were preserved."
                },
                {
                    diagnostics => diagnostics.ReportMissingLessonTopics(
                        course: "Synthetic course",
                        @group: "IA2501",
                        2,
                        GroupPartitionKey.All),
                    RegistrySyncDiagnosticKind.MissingLessonTopics,
                    "2 lessons have no configured topic for whole group. Add or correct the topic source for this course/group partition; text is copied without translation."
                },
                {
                    diagnostics => diagnostics.ReportMissingRegistryGroup(
                        course: "Synthetic course",
                        @group: "IA2501",
                        GroupPartitionKey.All),
                    RegistrySyncDiagnosticKind.MissingRegistryGroup,
                    "No registry destination matched the scheduled partition whole group. Ask the registry administrator to add or correct its group assignment."
                },
                {
                    diagnostics => diagnostics.ReportAttendanceStudentsNotInRegistry(
                        course: "Synthetic course",
                        @group: "IA2501",
                        [NameHelper.Parse("Popescu Ion"), NameHelper.Parse("Rusu Ana")],
                        LessonType.Lab,
                        GroupPartitionKey.All),
                    RegistrySyncDiagnosticKind.AttendanceStudentsNotInRegistry,
                    "2 attendance students have no matching student in any relevant registry roster for Lab (whole group): Popescu Ion; Rusu Ana. Check enrollment and name mappings."
                },
                {
                    diagnostics => diagnostics.ReportUnmatchedAttendanceStudents(
                        course: "Synthetic course",
                        @group: "IA2501",
                        [NameHelper.Parse("Popescu Ion"), NameHelper.Parse("Rusu Ana")],
                        lessonTypes: "Lab",
                        partitions: "whole group"),
                    RegistrySyncDiagnosticKind.UnmatchedAttendanceStudents,
                    "2 registry students have no matching attendance row after all relevant rosters were considered for Lab (whole group): Popescu Ion; Rusu Ana. Check attendance worksheets and name mappings."
                },
                {
                    diagnostics => diagnostics.ReportUnsafeRegistryLabCleanup(
                        course: "Synthetic course",
                        @group: "IA2501",
                        columnCount: 2,
                        markCount: 3,
                        new Uri("https://example.test/list")),
                    RegistrySyncDiagnosticKind.UnsafeRegistryLabCleanup,
                    "Selected 2 laboratory columns and 3 recorded attendance marks for deletion from https://example.test/list. Other lesson types are preserved."
                },
                {
                    diagnostics =>
                        diagnostics.ReportUnmatchedRegistryCourse(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.UnmatchedRegistryCourse,
                    "Add or correct the course mapping in the schedule configuration."
                },
                {
                    diagnostics =>
                        diagnostics.ReportUnmatchedRegistryGroup(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.UnmatchedRegistryGroup,
                    "Check the parent group, language, and subgroup mapping in the schedule configuration."
                },
                {
                    diagnostics =>
                        diagnostics.ReportUnrecognizedRegistryGroup(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.UnrecognizedRegistryGroup,
                    "Correct the registry group label or add support for its format."
                },
                {
                    diagnostics =>
                        diagnostics.ReportRegistryAssignmentMismatch(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.RegistryAssignmentMismatch,
                    "The registry destination has no matching lessons assigned to this teacher. Check its assignment."
                },
                {
                    diagnostics => diagnostics.ReportEmptyRegistryRoster(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.EmptyRegistryRoster,
                    "The registry destination has no student rows. Ask the registry administrator to check its enrollment; absences cannot be copied to an empty roster."
                },
                {
                    diagnostics => diagnostics.ReportMissingScheduleDates(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.MissingScheduleDates,
                    "Configure the semester date range for this group's grade, attendance mode, and qualification before synchronizing it."
                },
                {
                    diagnostics =>
                        diagnostics.ReportRegistryLabRoutingUnresolved(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.RegistryLabRoutingUnresolved,
                    "Unresolved lab routing prevents intake cleanup."
                },
                {
                    diagnostics =>
                        diagnostics.ReportRegistryLabFallbackPreserved(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.RegistryLabFallbackPreserved,
                    "This list is needed for scoped fallback labs. Intake cleanup leaves its lesson columns and attendance unchanged."
                },
                {
                    diagnostics =>
                        diagnostics.ReportAmbiguousRegistryDestination(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.AmbiguousRegistryDestination,
                    "Multiple equally specific registry lists cover this lab audience. Correct the assignments; this audience was not routed by list order."
                },
                {
                    diagnostics =>
                        diagnostics.ReportMissingRegistryCourse(course: "Synthetic course", @group: "IA2501"),
                    RegistrySyncDiagnosticKind.MissingRegistryCourse,
                    "Ask the registry administrator to assign this scheduled course to the teacher."
                },
            };
        }
    }

    [Theory]
    [MemberData(nameof(Reports))]
    public void ReportMethodsOwnCategoryAndWording(
        Action<RegistrySyncDiagnostics> report,
        RegistrySyncDiagnosticKind kind,
        string detail)
    {
        var diagnostics = new RegistrySyncDiagnostics();
        report(diagnostics);
        var single = Assert.Single(diagnostics.Items);
        Assert.Equal(expected: kind, actual: single.Kind);
        Assert.Equal(expected: "Synthetic course", actual: single.Course);
        Assert.Equal(expected: "IA2501", actual: single.Group);
        Assert.Equal(expected: detail, actual: single.Detail);
    }

    [Fact]
    public void IdenticalReportsCollapseIntoOneItemAndFireReportedOnce()
    {
        var reported = new List<RegistrySyncDiagnostic>();
        var diagnostics = new RegistrySyncDiagnostics();
        diagnostics.Reported += reported.Add;
        diagnostics.ReportMissingRegistryCourse(
            course: "Synthetic course",
            @group: "IA2501");
        diagnostics.ReportMissingRegistryCourse(
            course: "Synthetic course",
            @group: "IA2501");
        Assert.Single(diagnostics.Items);
        Assert.Single(reported);
    }

    [Fact]
    public void DifferingReportsStaySeparateEvenForOneKind()
    {
        var diagnostics = new RegistrySyncDiagnostics();
        diagnostics.ReportMissingScheduleDates(course: "Synthetic course", @group: "IA2501");
        diagnostics.ReportMissingScheduleDates(course: "Synthetic course", @group: "IA2502");
        Assert.Equal(expected: 2, actual: diagnostics.Items.Count);
    }

    [Theory]
    [InlineData(RegistrySyncDiagnosticKind.MissingRegistryCourse, "\"missing-registry-course\"")]
    [InlineData(RegistrySyncDiagnosticKind.MissingRegistryGroup, "\"missing-registry-group\"")]
    [InlineData(RegistrySyncDiagnosticKind.UnmatchedRegistryCourse, "\"unmatched-registry-course\"")]
    [InlineData(RegistrySyncDiagnosticKind.UnmatchedRegistryGroup, "\"unmatched-registry-group\"")]
    [InlineData(RegistrySyncDiagnosticKind.UnrecognizedRegistryGroup, "\"unrecognized-registry-group\"")]
    [InlineData(RegistrySyncDiagnosticKind.AmbiguousRegistryDestination, "\"ambiguous-registry-destination\"")]
    [InlineData(RegistrySyncDiagnosticKind.AttendanceStudentsNotInRegistry, "\"attendance-students-not-in-registry\"")]
    [InlineData(RegistrySyncDiagnosticKind.UnmatchedAttendanceStudents, "\"unmatched-attendance-students\"")]
    [InlineData(RegistrySyncDiagnosticKind.RegistryAssignmentMismatch, "\"registry-assignment-mismatch\"")]
    [InlineData(RegistrySyncDiagnosticKind.RegistryPartitionMismatch, "\"registry-partition-mismatch\"")]
    [InlineData(RegistrySyncDiagnosticKind.RegistryDeletionRefused, "\"registry-deletion-refused\"")]
    [InlineData(RegistrySyncDiagnosticKind.EmptyRegistryRoster, "\"empty-registry-roster\"")]
    [InlineData(RegistrySyncDiagnosticKind.MissingScheduleDates, "\"missing-schedule-dates\"")]
    [InlineData(RegistrySyncDiagnosticKind.MissingAttendanceSheet, "\"missing-attendance-sheet\"")]
    [InlineData(RegistrySyncDiagnosticKind.AmbiguousRegistryAssessment, "\"ambiguous-registry-assessment\"")]
    [InlineData(RegistrySyncDiagnosticKind.MissingLessonTopics, "\"missing-lesson-topics\"")]
    [InlineData(RegistrySyncDiagnosticKind.RegistryLabRoutingUnresolved, "\"registry-lab-routing-unresolved\"")]
    [InlineData(RegistrySyncDiagnosticKind.RegistryLabFallbackPreserved, "\"registry-lab-fallback-preserved\"")]
    [InlineData(RegistrySyncDiagnosticKind.UnsafeRegistryLabCleanup, "\"unsafe-registry-lab-cleanup\"")]
    public void CategoryJsonKeepsTheWireName(
        RegistrySyncDiagnosticKind kind,
        string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        Assert.Equal(
            expected: json,
            actual: JsonSerializer.Serialize(kind, options));
    }

    [Fact]
    public void CategoriesMissingAWireNameAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((RegistrySyncDiagnosticKind) 100).ToWire());
    }
}

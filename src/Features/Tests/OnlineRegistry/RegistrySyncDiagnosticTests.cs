using System.Text.Json;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistrySyncDiagnosticTests
{
    /// <summary>Each report method pins one event's category; coordinates pass through.</summary>
    public static TheoryData<Func<string, string?, string?, RegistrySyncDiagnosticKind>> ReportKinds =>
        new()
        {
            // Course and group coverage.
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportMissingRegistryCourse(course, group, detail), RegistrySyncDiagnosticKind.MissingRegistryCourse },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportMissingRegistryGroup(course, group, detail), RegistrySyncDiagnosticKind.MissingRegistryGroup },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportUnmatchedRegistryCourse(course, group, detail), RegistrySyncDiagnosticKind.UnmatchedRegistryCourse },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportUnmatchedRegistryGroup(course, group, detail), RegistrySyncDiagnosticKind.UnmatchedRegistryGroup },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportUnrecognizedRegistryGroup(course, group, detail), RegistrySyncDiagnosticKind.UnrecognizedRegistryGroup },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportAmbiguousRegistryDestination(course, group, detail), RegistrySyncDiagnosticKind.AmbiguousRegistryDestination },
            // Attendance-student coverage.
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportAttendanceStudentsNotInRegistry(course, group, detail), RegistrySyncDiagnosticKind.AttendanceStudentsNotInRegistry },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportUnmatchedAttendanceStudents(course, group, detail), RegistrySyncDiagnosticKind.UnmatchedAttendanceStudents },
            // Lesson import.
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportRegistryAssignmentMismatch(course, group, detail), RegistrySyncDiagnosticKind.RegistryAssignmentMismatch },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportRegistryPartitionMismatch(course, group, detail), RegistrySyncDiagnosticKind.RegistryPartitionMismatch },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportRegistryDeletionRefused(course, group, detail), RegistrySyncDiagnosticKind.RegistryDeletionRefused },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportEmptyRegistryRoster(course, group, detail), RegistrySyncDiagnosticKind.EmptyRegistryRoster },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportMissingScheduleDates(course, group, detail), RegistrySyncDiagnosticKind.MissingScheduleDates },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportMissingAttendanceSheet(course, group, detail), RegistrySyncDiagnosticKind.MissingAttendanceSheet },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportAmbiguousRegistryAssessment(course, group, detail), RegistrySyncDiagnosticKind.AmbiguousRegistryAssessment },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportMissingLessonTopics(course, group, detail), RegistrySyncDiagnosticKind.MissingLessonTopics },
            // Intake lab cleanup.
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportRegistryLabRoutingUnresolved(course, group, detail), RegistrySyncDiagnosticKind.RegistryLabRoutingUnresolved },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportRegistryLabFallbackPreserved(course, group, detail), RegistrySyncDiagnosticKind.RegistryLabFallbackPreserved },
            { (course, group, detail) => new RegistrySyncDiagnostics().ReportUnsafeRegistryLabCleanup(course, group, detail), RegistrySyncDiagnosticKind.UnsafeRegistryLabCleanup },
        };

    [Theory]
    [MemberData(nameof(ReportKinds))]
    public void ReportMethodsOwnTheCategory(
        Func<string, string?, string?, RegistrySyncDiagnosticKind> report,
        RegistrySyncDiagnosticKind kind)
    {
        var diagnostics = new RegistrySyncDiagnostics();
        report("Synthetic course", "IA2501", "Synthetic detail.");
        var single = Assert.Single(diagnostics.Items);
        Assert.Equal(expected: kind, actual: single.Kind);
        Assert.Equal(expected: "Synthetic course", actual: single.Course);
        Assert.Equal(expected: "IA2501", actual: single.Group);
        Assert.Equal(expected: "Synthetic detail.", actual: single.Detail);
    }

    [Fact]
    public void IdenticalReportsCollapseIntoOneItemAndFireReportedOnce()
    {
        var reported = new List<RegistrySyncDiagnostic>();
        var diagnostics = new RegistrySyncDiagnostics();
        diagnostics.Reported += reported.Add;
        diagnostics.ReportMissingRegistryCourse(
            "Synthetic course",
            "IA2501",
            "Ask the registry administrator.");
        diagnostics.ReportMissingRegistryCourse(
            "Synthetic course",
            "IA2501",
            "Ask the registry administrator.");
        Assert.Single(diagnostics.Items);
        Assert.Single(reported);
    }

    [Fact]
    public void DifferingReportsStaySeparateEvenForOneKind()
    {
        var diagnostics = new RegistrySyncDiagnostics();
        diagnostics.ReportMissingScheduleDates("Synthetic course", "IA2501", "First.");
        diagnostics.ReportMissingScheduleDates("Synthetic course", "IA2502", "Second.");
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
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ((RegistrySyncDiagnosticKind) 100).ToWire());
    }
}

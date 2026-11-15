using ScheduleLib;
using ScheduleLib.Dates;

namespace ScheduleFromDoc.Tests;

public sealed class ScheduleSourceSanityTests
{
    [Fact]
    public async Task Schedule2026Semester1MatchesReadableSnapshot()
    {
        using var helper = new IntegrationTestHelper(2026, Semester.Sem1);

        await helper.InitializeSchedule();

        var schedule = helper.GetScheduleFromSourceOfTruth();
        await helper.ScheduleVerify(schedule, canonicalOrder: true);

        Assert.NotEmpty(schedule.WeeklyLessons);
        var dual = helper.GetScheduleFromSourceOfTruth()
            .EnumerateGroups()
            .Where(group => group.Item.AttendanceMode.IsDual())
            .ToArray();
        Assert.Equal(
            expected: new[]
            {
                "DU-IA2501",
                "DU-IA2601",
            },
            actual: dual.Select(group => group.Item.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.All(dual, group => Assert.Equal(expected: AttendanceMode.DualZi, actual: group.Item.AttendanceMode));
    }
}

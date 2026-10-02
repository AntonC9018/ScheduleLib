using ScheduleLib;
using ScheduleLib.Dates;

namespace ScheduleFromDoc.Tests;

public sealed class ScheduleSourceSanityTests
{
    [Fact]
    public async Task Schedule2026Semester1PassesSanityChecks()
    {
        using var helper = new IntegrationTestHelper(2026, Semester.Sem1);

        await helper.InitializeSchedule();

        Assert.NotEmpty(helper.GetScheduleFromSourceOfTruth().WeeklyLessons);
    }
}

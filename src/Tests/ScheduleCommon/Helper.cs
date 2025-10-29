using ScheduleLib;
using ScheduleLib.Builders;

namespace Tests.ScheduleCommon;

public static class ScheduleTestHelper
{
    public static async Task<ScheduleBuilder> CreateTestSchedule()
    {
        using var cts = TestHelper.CreateCts();
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2025);
        await using var scheduleJson = File.OpenRead("data/schedule_2025_1.json");
        var model = await ScheduleSerializer.Deserialize(scheduleJson, cts.Token);
        ScheduleSerializer.AddToBuilder(builder, model);
        return builder;
    }

    public static FilteredSchedule FilterForTeacher(
        Schedule schedule,
        LookupFacade lookup,
        TeacherBuilderModel.NameModel name)
    {
        var teacherId = lookup.Teacher(name)!.Value;
        var filteredSchedule = schedule.Filter(new()
        {
            TeacherFilter =
            {
                IncludeIds = [teacherId],
            },
            PeriodFilter =
            {
                PeriodId = schedule.LatestPeriodId(),
            },
        });
        return filteredSchedule;
    }

    public static async Task<FilteredSchedule> CreateScheduleForTeacher(TeacherBuilderModel.NameModel name)
    {
        var s = await CreateTestSchedule();
        var schedule = s.Build();
        var ret = FilterForTeacher(schedule, s.Lookup(), name);
        return ret;
    }
}

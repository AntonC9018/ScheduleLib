using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.DependencyInjection.ScopePlans;
using ScheduleLib.Parsing.CourseName;

namespace Tests.ScheduleCommon;

/// <summary>A schedule built by a test, loaded instead of the schedule sources.</summary>
public sealed class FixedSchedule(Schedule schedule)
{
    public Schedule Schedule => schedule;

    public static void Register(IServiceCollection services, Schedule schedule)
    {
        services.AddSingleton(new FixedSchedule(schedule));
        services.AddInitialized<LoadedSchedule, FixedScheduleInitializer>();
    }
}

public sealed class FixedScheduleInitializer(FixedSchedule fixedSchedule, CourseNameUnifierConfig courseNames)
    : IInitializer<LoadedSchedule>
{
    public ValueTask<LoadedSchedule> InitializeAsync(CancellationToken cancellationToken)
    {
        var loaded = LoadedSchedule.Create(fixedSchedule.Schedule, courseNames);
        return ValueTask.FromResult(loaded);
    }
}

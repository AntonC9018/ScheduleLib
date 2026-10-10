using System.Diagnostics;
using AutoConstructor.Attributes;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Core;

public sealed class ScheduleProvider
{
    private readonly ScheduleBuilder _builder;

    public ScheduleProvider(ScheduleBuilder builder)
    {
        _builder = builder;
    }

    public Schedule Get()
    {
        var schedule = _builder.Build();
        return schedule;
    }
}

public static class ServiceProviderHelper
{
    extension(IServiceProvider serviceProvider)
    {
        public FilteredSchedule LatestPeriodSchedule()
        {
            var provider = serviceProvider.GetRequiredService<LatestPeriodFilteredScheduleProvider>();
            return provider.Get();
        }

        public FilteredSchedule ScopedSchedule()
        {
            var provider = serviceProvider.GetRequiredService<ScopeFilteredScheduleProvider>();
            return provider.Get();
        }
    }
}

[AutoConstructor]
public sealed partial class CurrentTeacherIdProvider
{
    private readonly DataProvider<TeacherLayerConfig> _configProvider;
    private readonly LookupFacade _lookup;

    public TeacherId Get()
    {
        var teacherConfig = _configProvider.Get();
        Debug.Assert(teacherConfig != null);
        var teacherName = teacherConfig.TeacherName;
        return TeacherProfileScheduleLookup.Find(_lookup, teacherName)
            ?? throw new InvalidOperationException($"Selected teacher '{teacherName}' is absent from this schedule.");
    }
}

/// <summary>Resolves a profile marker to its schedule teacher without changing the profile identity.</summary>
public static class TeacherProfileScheduleLookup
{
    public static TeacherId? Find(LookupFacade lookup, Name profileName)
    {
        if (lookup.Teacher(profileName.ToNameModel()) is { } found)
            return found;

        // The registry credential profile predates the spelling in the current schedule.
        return profileName.ToString() == "Visnevschi Vlada"
            ? lookup.Teacher(NameHelper.Parse("Vișnevschii Vlada").ToNameModel())
            : null;
    }
}

[AutoConstructor]
public sealed partial class LatestPeriodFilteredScheduleProvider
{
    private readonly Schedule _schedule;

    public FilteredSchedule Get()
    {
        var schedule = _schedule;
        var filter = FilterHelper.Builder()
            .WithLatestPeriod(schedule);
        var filtered = schedule.Filter(filter);
        return filtered;
    }
}

[AutoConstructor]
public sealed partial class ScopeFilteredScheduleProvider
{
    private readonly Schedule _schedule;
    private readonly CurrentTeacherIdProvider _idProvider;

    public FilteredSchedule Get()
    {
        var schedule = _schedule;
        var teacherId = _idProvider.Get();

        var filter = FilterHelper.Builder()
            .WithLatestPeriod(schedule)
            .WithTeacher(teacherId);
        var filtered = schedule.Filter(filter);
        return filtered;
    }
}

public interface IScheduleInitializer
{
    public Task Initialize(
        ScheduleBuilder builder,
        CancellationToken cancellationToken);
}

public static class InitializationHelper
{
    public static async Task InitializeSchedule(
        this IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        var i = sp.GetRequiredService<IScheduleInitializer>();
        var scheduleBuilder = sp.GetRequiredService<ScheduleBuilder>();
        await i.Initialize(scheduleBuilder, cancellationToken);
    }
}

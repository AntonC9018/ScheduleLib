using System.Diagnostics;
using AutoConstructor.Attributes;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib.DependencyInjection.ScopePlans;
using ScheduleLib.Application.Config;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Core;

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
            ?? throw new SelectedTeacherAbsentFromScheduleException(teacherName);
    }
}

/// <summary>Resolves a profile marker to its schedule teacher without changing the profile identity.</summary>
public static class TeacherProfileScheduleLookup
{
    public static TeacherId? Find(LookupFacade lookup, Name profileName)
    {
        var name = profileName.ToNameModel();
        if (lookup.Teacher(name) is { } found)
        {
            return found;
        }

        // A profile may predate the spelling used by the current schedule.
        if (lookup.RemapTeacherName(ref name) == TeacherNameRemapStatus.None)
        {
            return null;
        }

        var remappedTeacher = lookup.Teacher(name);
        if (remappedTeacher is not null)
        {
            return remappedTeacher;
        }

        return null;
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

/// <summary>
/// Opens a scope with the schedule loaded, for commands that still resolve services from a scope.
/// Removed once every command runs through a scope plan (#247).
/// </summary>
public sealed class ScheduleScope : IAsyncDisposable
{
    private readonly AsyncServiceScope _invocation;
    private readonly PlannedScope<LoadedSchedule> _scope;

    private ScheduleScope(AsyncServiceScope invocation, PlannedScope<LoadedSchedule> scope)
    {
        _invocation = invocation;
        _scope = scope;
    }

    public IServiceProvider ServiceProvider => _scope.Services;

    public static async Task<ScheduleScope> Open(IServiceProvider root, CancellationToken cancellationToken)
    {
        var invocation = root.CreateAsyncScope();
        try
        {
            var forks = invocation.ServiceProvider.GetRequiredService<ScopeForkFactory<LoadedSchedule>>();
            var scope = await forks.OpenAsync(supply: null, cancellationToken);
            return new(invocation, scope);
        }
        catch
        {
            await invocation.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _invocation.DisposeAsync();
    }
}

public static class ScheduleScopeHelper
{
    public static Task<ScheduleScope> OpenScheduleScope(
        this IServiceProvider root,
        CancellationToken cancellationToken)
    {
        return ScheduleScope.Open(root, cancellationToken);
    }
}

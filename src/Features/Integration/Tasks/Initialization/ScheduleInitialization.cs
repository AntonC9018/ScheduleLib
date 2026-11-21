using System.Diagnostics;
using AutoConstructor.Attributes;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
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
/// A planned scope opened straight from a root container, through an invocation scope of its own,
/// for hosts and commands that still resolve services from a scope.
/// Removed once every command runs through a scope plan (#247).
/// </summary>
public sealed class HostScope<THandler> : IAsyncDisposable
    where THandler : notnull
{
    private readonly InvocationScope _invocation;
    private readonly PlannedScope<THandler> _scope;

    private HostScope(InvocationScope invocation, PlannedScope<THandler> scope)
    {
        _invocation = invocation;
        _scope = scope;
    }

    public IServiceProvider ServiceProvider => _scope.Services;

    public THandler Handler => _scope.Handler;

    public static async Task<HostScope<THandler>> Open(
        IServiceProvider root,
        Action<ScopeInputs> supply,
        CancellationToken cancellationToken)
    {
        // CA2000 does not see ownership pass to the returned HostScope; failures dispose below.
#pragma warning disable CA2000
        var invocation = await InvocationScope.OpenAsync(root);
#pragma warning restore CA2000
        try
        {
            var forks = invocation.Services.GetRequiredService<ScopeForkFactory<THandler>>();
            var scope = await forks.OpenAsync(supply, cancellationToken);
            return new(invocation, scope);
        }
        catch (Exception openFailure)
        {
            var failures = new CleanupFailures();
            await failures.Run(invocation.DisposeAsync);
            failures.ThrowIfAnyAlongside(openFailure);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new CleanupFailures();
        await failures.Run(_scope.DisposeAsync);
        await failures.Run(_invocation.DisposeAsync);
        failures.ThrowIfAny();
    }
}

public static class HostScopeHelper
{
    public static Task<HostScope<LoadedSchedule>> OpenScheduleScope(
        this IServiceProvider root,
        ScheduleSource source,
        CancellationToken cancellationToken)
    {
        return HostScope<LoadedSchedule>.Open(root, inputs => inputs.Add(source), cancellationToken);
    }

    public static Task<HostScope<DataProvider>> OpenMarkerScope(
        this IServiceProvider root,
        TeacherLayerConfig marker,
        CancellationToken cancellationToken)
    {
        return HostScope<DataProvider>.Open(root, inputs => inputs.Add(marker), cancellationToken);
    }
}

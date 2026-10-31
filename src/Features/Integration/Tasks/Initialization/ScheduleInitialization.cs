using System.Diagnostics;
using AutoConstructor.Attributes;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Core;

public sealed class ScheduleProvider
{
    private readonly ScheduleBuilder _builder;
    private readonly IOptions<ScheduleDerivationOptions> _options;
    private readonly ILogger<ScheduleProvider> _logger;
    private readonly Lazy<Schedule> _schedule;

    public ScheduleProvider(
        ScheduleBuilder builder,
        IOptions<ScheduleDerivationOptions> options,
        ILogger<ScheduleProvider> logger)
    {
        _builder = builder;
        _options = options;
        _logger = logger;
        _schedule = new(Build);
    }

    public Schedule Get()
    {
        return _schedule.Value;
    }

    private Schedule Build()
    {
        var raw = _builder.Build();
        // Implicit partition assignments can append lessons during Build. Refresh
        // their raw IDs before any registry consumer uses the course lookup.
        _builder.RefreshLookup();
        var schedule = LabSubGroupMerges.Project(raw, _options.Value.AutoMergeLabSubGroups);
        foreach (var diagnostic in schedule.LabSubGroupMergeDiagnostics)
        {
            _logger.LogWarning(
                "Cannot infer a lab subgroup merge for {Course}, {Group}, {Specialization}, {Alternative}: {Reason}",
                schedule.Get(diagnostic.Course).FullName,
                schedule.Get(diagnostic.Group).Name,
                diagnostic.Specialization.Value,
                diagnostic.Alternative.Value,
                diagnostic.Reason);
        }

        if (!schedule.AutomaticLabSubGroupMerges.IsEmpty)
        {
            _logger.LogInformation(
                "Derived {Count} automatic lab subgroup merges for the semester",
                schedule.AutomaticLabSubGroupMerges.Length);
        }

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
        return lookup.Teacher(name);
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

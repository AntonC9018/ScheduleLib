using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using ScheduleLib.Application.Config;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

public sealed record SettingsSelection(string? ProjectPath = null, string? Profile = null, string? CalendarName = null)
{
    public static SettingsSelection From(SettingsArguments? settings, ConfigOverrideArguments? overrides = null)
    {
        return new(
            ProjectPath: settings?.Project,
            Profile: settings?.Profile,
            CalendarName: overrides?.CalendarName);
    }
}

// The tree is populated once when the root container is built; each load only overlays its own nodes.
public sealed class SettingsLoader(TreeBuilder tree)
{
    public Task<ResolvedSettings> Load(SettingsSelection selection, CancellationToken cancellationToken)
    {
        return CliSettings.LoadCore(
            tree,
            selection,
            invocationDirectory: null,
            userFile: null,
            cancellationToken);
    }
}

public sealed class ResolvedSettingsInitializer(SettingsSelection selection, SettingsLoader loader)
    : IInitializer<ResolvedSettings>
{
    public async ValueTask<ResolvedSettings> InitializeAsync(CancellationToken cancellationToken) =>
        await loader.Load(selection, cancellationToken);
}

public sealed class SettingsTeacherLayerFactory(ResolvedSettings settings) : IFactory<TeacherLayerConfig>
{
    public TeacherLayerConfig Create() => settings.CreateTeacherLayer();
}

public sealed class SettingsMarkerDataHelperFactory(ResolvedSettings settings) : IFactory<IMarkerDataHelper>
{
    public IMarkerDataHelper Create() => settings;
}

public sealed class SettingsScheduleDerivationFactory(ResolvedSettings settings) : IFactory<ScheduleDerivationOptions>
{
    public ScheduleDerivationOptions Create() => settings.CreateDerivationOptions();
}

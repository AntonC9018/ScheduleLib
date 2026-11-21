using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

// Unvalidated; InvocationDirectory is captured so later directory changes do not move relative paths.
public sealed record ScheduleSourceSelection(
    string? DataDirectory,
    string? CacheDirectory,
    bool NoCache,
    string InvocationDirectory)
{
    public static ScheduleSourceSelection From(SourceArguments source)
    {
        return new(
            DataDirectory: source.DataDirectory,
            CacheDirectory: source.CacheDirectory,
            NoCache: source.NoCache,
            InvocationDirectory: Environment.CurrentDirectory);
    }

    public ScheduleSource Resolve()
    {
        string ResolveDataDirectory()
        {
            if (DataDirectory is { } path)
            {
                return CliRuntime.ResolvePath(path: path, basePath: InvocationDirectory);
            }

            return Path.Combine(AppContext.BaseDirectory, "data");
        }

        string ResolveCacheDirectory()
        {
            if (CacheDirectory is { } path)
            {
                return CliRuntime.ResolvePath(path: path, basePath: InvocationDirectory);
            }

            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(
                localApplicationData,
                "ScheduleLib",
                "cache");
        }

        var dataDirectory = ResolveDataDirectory();
        var cacheDirectory = ResolveCacheDirectory();

        if (!Directory.Exists(dataDirectory))
        {
            throw new CliRuntime.ScheduleSourceDirectoryNotFoundException(dataDirectory);
        }

        return new(DataDirectory: dataDirectory, CacheDirectory: cacheDirectory, UseCache: !NoCache);
    }

    public static void AddScopeValues(IServiceCollection services)
    {
        services.AddSeed<ScheduleSourceSelection>();
        services.RemoveScopeValue<ScheduleSource>();
        services.AddDerived<ScheduleSource, ScheduleSourceFactory>();
    }
}

/// <summary>
/// Resolves the schedule source of a scope once its settings have loaded, so an invalid settings
/// selection is reported before an invalid source, as it was when commands loaded settings first.
/// </summary>
public sealed class ScheduleSourceFactory : IFactory<ScheduleSource>
{
    private readonly ScheduleSourceSelection _selection;

    // settings is not read: requiring it orders the source after the settings initializer in the plan.
    public ScheduleSourceFactory(ScheduleSourceSelection selection, ResolvedSettings settings) =>
        _selection = selection;

    public ScheduleSource Create() => _selection.Resolve();
}

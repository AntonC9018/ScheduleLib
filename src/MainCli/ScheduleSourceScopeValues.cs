using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

/// <summary>
/// The schedule source options of an invocation, unvalidated, with the directory their relative
/// paths resolve from. A seed.
/// </summary>
public sealed record ScheduleSourceSelection(
    string? DataDirectory,
    string? CacheDirectory,
    bool NoCache,
    string InvocationDirectory)
{
    /// <summary>The options of <paramref name="source"/>, relative to the current directory.</summary>
    public static ScheduleSourceSelection From(SourceArguments source)
    {
        return new(
            DataDirectory: source.DataDirectory,
            CacheDirectory: source.CacheDirectory,
            NoCache: source.NoCache,
            InvocationDirectory: Environment.CurrentDirectory);
    }

    /// <summary>
    /// Validates the options and resolves their paths. Without a data directory the packaged
    /// resources are used, without a cache directory the ScheduleLib user cache.
    /// </summary>
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

    /// <summary>
    /// The selection as a seed, replacing the <see cref="ScheduleSource"/> seed with a value
    /// derived from it.
    /// </summary>
    public static void AddScopeValues(IServiceCollection services)
    {
        services.AddSeed<ScheduleSourceSelection>();
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

    /// <param name="selection">The source options to resolve.</param>
    /// <param name="settings">
    /// Not read: requiring it orders the source after the settings initializer in the plan.
    /// </param>
    public ScheduleSourceFactory(ScheduleSourceSelection selection, ResolvedSettings settings) =>
        _selection = selection;

    public ScheduleSource Create() => _selection.Resolve();
}

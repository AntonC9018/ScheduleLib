using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

/// <summary>Shared source/cache setup for query and export slices. CLI paths resolve from the
/// invocation directory.</summary>
public static class CliRuntime
{
    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new EmptyDirectoryPathException(path);
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// The schedule source an invocation selects. Relative paths resolve from the invocation
    /// directory now, so later directory changes do not move them.
    /// </summary>
    public static ScheduleSource ResolveSource(SourceArguments source)
    {
        string ResolveDataDirectory()
        {
            if (source.DataDirectory is { } path)
            {
                return ResolvePath(path);
            }

            return Path.Combine(AppContext.BaseDirectory, "data");
        }

        string ResolveCacheDirectory()
        {
            if (source.CacheDirectory is { } path)
            {
                return ResolvePath(path);
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
            throw new ScheduleSourceDirectoryNotFoundException(dataDirectory);
        }

        return new(dataDirectory, cacheDirectory, UseCache: !source.NoCache);
    }

    /// <summary>A container of its own, for commands that do not run through the root container yet.</summary>
    public static IServiceCollection CreateServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        AddCliServices(services);
        return services;
    }

    /// <summary>The CLI's logging and Google authorization, replacing the defaults of <paramref name="services"/>.</summary>
    public static void AddCliServices(IServiceCollection services)
    {
        CliLogging.Register(services);
        GoogleAuthentication.Register(services);
    }

    internal sealed class EmptyDirectoryPathException : ArgumentException
    {
        internal EmptyDirectoryPathException(string? directoryPath)
            : base("Directory paths must not be empty.")
            => DirectoryPath = directoryPath;

        public string? DirectoryPath { get; }
    }

    internal sealed class ScheduleSourceDirectoryNotFoundException : DirectoryNotFoundException
    {
        internal ScheduleSourceDirectoryNotFoundException(string sourcePath)
            : base($"Schedule source directory does not exist: {sourcePath}")
            => SourcePath = sourcePath;

        public string SourcePath { get; }
    }
}

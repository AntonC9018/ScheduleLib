using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

/// <summary>Shared source/cache setup for query and export slices. Project settings
/// can supply their resolved root; CLI paths resolve from the invocation directory.</summary>
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

    public static IServiceCollection CreateServices(
        SourceArguments source,
        Action<IServiceCollection> configure,
        string? projectDirectory = null)
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

        var services = new ServiceCollection();
        configure(services);
        GoogleAuthentication.Register(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            x.DataDirectory = dataDirectory;
            x.CacheDirectory = cacheDirectory;
            x.UseCache = !source.NoCache;
        });
        return services;
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

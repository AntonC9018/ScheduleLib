using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

/// <summary>Shared source/cache setup for query and export slices. Project settings
/// can supply their resolved root; CLI paths resolve from the invocation directory.</summary>
public static class CliRuntime
{
    public static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Directory paths must not be empty.");
        return Path.GetFullPath(path);
    }

    public static IServiceCollection CreateServices(SourceArguments source, Action<IServiceCollection> configure, string? projectDirectory = null)
    {
        var dataDirectory = source.DataDirectory is { } path ? ResolvePath(path) : Path.Combine(AppContext.BaseDirectory, "data");
        if (source.CacheDirectory is { } selectedCache) ResolvePath(selectedCache);
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"Schedule source directory does not exist: {dataDirectory}");
        var services = new ServiceCollection();
        configure(services);
        GoogleAuthentication.Register(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            x.DataDirectory = dataDirectory;
            x.CacheDirectory = source.CacheDirectory is { } cache ? ResolvePath(cache)
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLib", "cache");
            x.UseCache = !source.NoCache;
        });
        return services;
    }
}

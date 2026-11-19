using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

/// <summary>Shared source/cache setup for query and export slices. CLI paths resolve from the
/// invocation directory.</summary>
public static class CliRuntime
{
    public static string ResolvePath(string path) => ResolvePath(path: path, basePath: Environment.CurrentDirectory);

    /// <summary>The full path of <paramref name="path"/>, relative to <paramref name="basePath"/>.</summary>
    public static string ResolvePath(string path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new EmptyDirectoryPathException(path);
        }

        return Path.GetFullPath(path: path, basePath: basePath);
    }

    /// <summary>
    /// The schedule source an invocation selects. Relative paths resolve from the invocation
    /// directory now, so later directory changes do not move them.
    /// </summary>
    public static ScheduleSource ResolveSource(SourceArguments source)
    {
        return ScheduleSourceSelection.From(source)
            .Resolve();
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

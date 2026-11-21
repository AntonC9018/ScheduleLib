using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public static class CliRuntime
{
    public static string ResolvePath(string path) => ResolvePath(path: path, basePath: Environment.CurrentDirectory);

    public static string ResolvePath(string path, string basePath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new EmptyDirectoryPathException(path);
        }

        return Path.GetFullPath(path: path, basePath: basePath);
    }

    // Relative paths resolve from the invocation directory now, so later directory changes do not move them.
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

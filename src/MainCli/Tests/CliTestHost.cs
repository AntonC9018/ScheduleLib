using CommandDotNet;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

internal static class CliTestHost
{
    public static string FixturesDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>
    /// <paramref name="services"/> replaces root registrations after the CLI's own;
    /// <paramref name="cancellationToken"/> stands in for the console's cancellation once the
    /// command is invoked. CommandDotNet skips a command whose token is already cancelled at that point.
    /// </summary>
    public static Task<int> Run(
        string[] args,
        Action<IServiceCollection>? services = null,
        CancellationToken cancellationToken = default,
        Action<AppRunner<Commands>>? runner = null)
    {
        Task<int> UseCancellation(CommandContext context, ExecutionDelegate next)
        {
            context.CancellationToken = cancellationToken;
            return next(context);
        }

        void ConfigureRunner(AppRunner<Commands> appRunner)
        {
            if (cancellationToken.CanBeCanceled)
            {
                appRunner.Configure(builder => builder.UseMiddleware(UseCancellation, MiddlewareSteps.InvokeCommand - 1));
            }

            runner?.Invoke(appRunner);
        }

        return CliHost.RunCore(args, new CliHostOverrides(services, ConfigureRunner));
    }

    public static void FixtureSchedule(IServiceCollection services)
    {
        services.Configure<ScheduleSourceLoaderOptions>(x =>
        {
            x.EnrichWithFullNames = false;
        });
        services.Configure<StudyYearOptions>(x =>
        {
            var studyYear = new StudyYear(2025);
            x.StudyYear = studyYear;
            x.Semester = Semester.Sem2;
        });
    }

    public static string[] UncachedSource(string? dataDirectory = null) =>
        ["--no-cache", "--data-dir", dataDirectory ?? FixturesDirectory];
}

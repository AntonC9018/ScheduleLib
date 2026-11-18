using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

public static class StorageProbe
{
    public static async Task<int> Main(string[] args)
    {
        if (args[0] == "export")
        {
            return await new FixtureExport().TeachersExcel(
                new()
                {
                    DataDirectory = args[1],
                    CacheDirectory = args[2],
                },
                new()
                {
                    Directory = args[3] == "-" ? null : args[3],
                },
                new()
                {
                    Json = true,
                });
        }

        if (args[0] == "query")
        {
            string[] noCache = args.Length > 3 ? ["--no-cache"] : [];
            return await CliTestHost.Run(
                ["query", "lessons", "--json", "--data-dir", args[1], "--cache-dir", args[2], .. noCache],
                CliTestHost.FixtureSchedule);
        }

        if (args[0] == "lock")
        {
            await using var lease = await LocalFileLock.Acquire(args[1], CancellationToken.None);
            Console.WriteLine("locked");
            await Task.Delay(Timeout.Infinite);
        }
        if (args[0] == "interrupt")
        {
            var lockPath = $"{args[1]}.lock";
            await using var lease = await LocalFileLock.Acquire(
                lockPath,
                CancellationToken.None,
                wait: true);
            await AtomicFile.Publish(
                args[1],
                async (stream, token) =>
                {
                    await stream.WriteAsync(new byte[1024 * 1024], token);
                    await stream.FlushAsync(token);
                    Console.WriteLine("staged");
                    await Task.Delay(Timeout.Infinite, token);
                },
                CancellationToken.None);
        }
        return 0;
    }

    private sealed class FixtureExport : ExportCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleSourceLoaderOptions>(x =>
            {
                var loaderOptions = x;
                loaderOptions.EnrichWithFullNames = false;
            });
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
        }
    }
}

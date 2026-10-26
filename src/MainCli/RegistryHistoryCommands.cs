using System.Text.Json;
using Anton.LayeredData.Retrieval;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Cli;

public sealed class RegistryHistoryArguments : IArgumentModel
{
    [Option(
        "year",
        Description =
            "Exact academic-year selector value, e.g. 2024/25, or current for the registry-selected year only. Omit to discover all offered years.")]
    public string? Year { get; set; }

    [Option(
        "local-directory",
        Description =
            "Required private output directory named .registry-history (ignored by Git). Only counts appear on stdout.")]
    public string? LocalDirectory { get; set; }
}

public interface IRegistryHistoryProvider
{
    Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(
        SettingsArguments settings,
        string? year,
        CancellationToken token);
}

public sealed partial class ConfiguredRegistryHistoryProvider : IRegistryHistoryProvider, IRegistryHistoryTopicsProvider
{
    public Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(
        SettingsArguments settings,
        string? year,
        CancellationToken token)
    {
        return WithHistoryContext(
            settings,
            client =>
            {
                var client2 = client;
                Uri index = new(RegistryScraping.BaseUrl + "LessonAttendance");
                var registryHistoryReader =
                    new RegistryHistoryReader(client2, index);
                string? year2 = year;
                var token2 = token;
                var discover =
                    registryHistoryReader.Discover(
                        year2,
                        token2);
                return discover;
            },
            token);
    }

    private static async Task<T> WithHistoryContext<T>(
        SettingsArguments settings,
        Func<HttpClient, Task<T>> read,
        CancellationToken token)
    {
        // A single destination-wide lease covers login and all reads, regardless of account/profile.
        string path1 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string path2 = "ScheduleLib";
        string path3 = "locks";
        string path4 = "registry-history-global.lock";
        var lockPath = Path.Combine(
            path1,
            path2,
            path3,
            path4);
        await using var lease = await LocalFileLock.Acquire(lockPath, token);
        using var resolved = await CliSettings.Load(settings, cancellationToken: token);
        var services = new ServiceCollection();
        AppConfiguration.ConfigureServices(services);
        resolved.ConfigureServices(services);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await using var scope = provider.CreateAsyncScope();
        Credentials credentials;
        try
        {
            credentials = scope.ServiceProvider.GetRequiredService<CredentialsResolver<BuiltRegistryConfig>>()
                .Get();

            bool CheckCondition()
            {
                if (credentials is null)
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(credentials.Login))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(credentials.Password))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
            {
                throw new InvalidOperationException();
            }
        }
        catch (InvalidOperationException)
        {
            throw new RegistryAuthenticationException(
                "Registry credentials are missing; configure the selected teacher's existing credential source.");
        }
        // No schedule initialization, token cache writes, or current-course/group matching.
        var delay = scope.ServiceProvider.GetService<DataProvider<BuiltRegistryConfig>>()?.Get()
                ?.HttpCallDelay
            ?? BuiltRegistryConfig.DefaultHttpCallDelay;
        using var context = await RegistryScraping.CreateHistoryContext(
            credentials,
            token,
            delay);
        return await read(context.HttpClient);
    }
}

public partial class RegistryCommands
{
    protected virtual IRegistryHistoryProvider CreateHistoryProvider() => new ConfiguredRegistryHistoryProvider();

    [Command(
        "history",
        Description =
            "Read-only raw course/group discovery for offered academic years. Login plus paced GETs only; private summaries require explicit local storage.")]
    public async Task<int> History(
        SettingsArguments settings,
        RegistryHistoryArguments history,
        ResultArguments resultArgs,
        CancellationToken cancellationToken = default)
    {
        using var context = new CliCommandContext(
            "registry history",
            resultArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var output = context.Output;
        var cancellation = context.Cancellation;
        try
        {
            int MissingInput()
            {
                return Finish(
                    CliExit.InvalidArguments,
                    "Select --profile and an explicit --local-directory named .registry-history.");
            }

            if (string.IsNullOrWhiteSpace(settings.Profile))
            {
                return MissingInput();
            }
            if (string.IsNullOrWhiteSpace(history.LocalDirectory))
            {
                return MissingInput();
            }

            try
            {
                _ = new RegistryHistoryTopicDirectory(history.LocalDirectory);
            }
            catch (Exception)
            {
                return Finish(
                    CliExit.InvalidArguments,
                    "Private local storage requires a regular directory named .registry-history.");
            }
            var directory = Path.GetFullPath(history.LocalDirectory);
            var summary = await CreateHistoryProvider()
                .Discover(
                    settings,
                    history.Year,
                    cancellation.Token);
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string path1 = directory;
            string text = $"summary-{context.RunId}.json";
            string path2 = text;
            var path = Path.Combine(path1, path2);
            // Unique files preserve existing authoritative topics and previous downloads.
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = new FileStream(path, options))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    summary,
                    cancellationToken: cancellation.Token);
            }
            // Stream per-year counts (year values are coordinator-visible; raw
            // course/group names stay in the private file and never reach stdout).
            for (var yearIndex = 0; yearIndex < summary.Count; yearIndex++)
            {
                var year = summary[yearIndex];
                var courseCount = year.Courses.Count;
                var groupCount = year.Courses.Sum((course) => course.Groups.Count);
                var cliOutput = output;
                string line = $"Year {year.Year}: {courseCount} courses, {groupCount} groups.";
                cliOutput.WriteHuman(line);
                var cliOutput2 = output;
                string id2 = yearIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string status2 = "completed";
                object item = new
                {
                    year = year.Year,
                    courses = courseCount,
                    groups = groupCount,
                };
                cliOutput2.EmitItem(
                    id: id2,
                    status: status2,
                    item);
            }
            return Finish(
                exit: CliExit.Success,
                null,
                years: summary.Count,
                courses: summary.Sum(x => x.Courses.Count),
                groups: summary.Sum(x => x.Courses.Sum(c => c.Groups.Count)));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return Finish(CliExit.Cancelled, "Cancelled.");
        }
        catch (RegistryAuthenticationException)
        {
            return Finish(CliExit.AuthenticationRequired, "Registry authentication configuration failed.");
        }
        catch (RegistryHistoryException error)
        {
            var status = error.StatusCode is { } code
                ? code.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "unknown";
            int exit2 = CliExit.IoFailure;
            string error2 =
                $"Registry history stopped: {error.Failure}; phase {error.Phase}; diagnostic {error.Diagnostic}; HTTP {status}.";
            int finish = Finish(
                exit2,
                error2);
            return finish;
        }
        catch (LocalOperationBusyException)
        {
            return Finish(CliExit.Busy, "Another local registry history operation is active.");
        }
        catch (Exception)
        {
            return Finish(
                CliExit.IoFailure,
                "Registry history discovery failed or stopped. No remote changes were submitted.");
        }

        int Finish(
            int exit,
            string? error,
            int years = 0,
            int courses = 0,
            int groups = 0)
        {
            string[] errors = [];
            if (error is not null)
            {
                errors = [error];
            }
            var data = new
            {
                years,
                courses,
                groups,
            };
            var status = CliCommandContext.Status(exit, success: "downloaded");
            var jsonStatus = CliCommandContext.Status(
                exit,
                success: "downloaded",
                cancellation: false);
            return context.Finish(new CliCompletion<object>(
                exit,
                errors,
                data)
            {
                Summary = data,
                Status = status,
                JsonStatus = jsonStatus,
                RenderHuman = _ =>
                {
                    if (exit == CliExit.Success)
                    {
                        var cliOutput = output;
                        string line = $"Saved local summary: {years} years, {courses} courses, {groups} groups.";
                        cliOutput.WriteHuman(
                            line);
                    }
                },
            });
        }
    }
}

using System.Text.Json;
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
    [Option("year", Description = "Exact academic-year selector value, e.g. 2024/25, or current for the registry-selected year only. Omit to discover all offered years.")]
    public string? Year { get; set; }
    [Option("local-directory", Description = "Required private output directory named .registry-history (ignored by Git). Only counts appear on stdout.")]
    public string? LocalDirectory { get; set; }
}

public interface IRegistryHistoryProvider
{
    Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(SettingsArguments settings, string? year, CancellationToken token);
}

public sealed class ConfiguredRegistryHistoryProvider : IRegistryHistoryProvider
{
    public async Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(SettingsArguments settings, string? year, CancellationToken token)
    {
        // A single destination-wide lease covers login and all reads, regardless of account/profile.
        var lockPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLib", "locks", "registry-history-global.lock");
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
            credentials = scope.ServiceProvider.GetRequiredService<CredentialsResolver<BuiltRegistryConfig>>().Get();
            if (credentials is null || string.IsNullOrWhiteSpace(credentials.Login) || string.IsNullOrWhiteSpace(credentials.Password))
                throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            throw new RegistryAuthenticationException("Registry credentials are missing; configure the selected teacher's existing credential source.");
        }
        // No schedule initialization, token cache writes, or current-course/group matching.
        using var context = await RegistryScraping.CreateHistoryContext(credentials, token);
        return await new RegistryHistoryReader(context.HttpClient, new(RegistryScraping.BaseUrl + "LessonAttendance")).Discover(year, token);
    }
}

public partial class RegistryCommands
{
    protected virtual IRegistryHistoryProvider CreateHistoryProvider() => new ConfiguredRegistryHistoryProvider();

    [Command("history", Description = "Read-only raw course/group discovery for offered academic years. Login plus paced GETs only; private summaries require explicit local storage.")]
    public async Task<int> History(SettingsArguments settings, RegistryHistoryArguments history, ResultArguments result,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (string.IsNullOrWhiteSpace(settings.Profile) || string.IsNullOrWhiteSpace(history.LocalDirectory))
                return Finish(2, "Select --profile and an explicit --local-directory named .registry-history.");
            var directory = Path.GetFullPath(history.LocalDirectory);
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory)) != ".registry-history")
                return Finish(2, "Private local storage must use a directory named .registry-history.");
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                return Finish(2, "Private local storage must be a regular directory.");
            var summary = await CreateHistoryProvider().Discover(settings, history.Year, cancellation.Token);
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var path = Path.Combine(directory, "summary-" + runId + ".json");
            // Unique files preserve existing authoritative topics and previous downloads.
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(path, options))
                await JsonSerializer.SerializeAsync(stream, summary, cancellationToken: cancellation.Token);
            return Finish(0, null, summary.Count, summary.Sum(x => x.Courses.Count), summary.Sum(x => x.Courses.Sum(c => c.Groups.Count)));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return Finish(130, "Cancelled."); }
        catch (RegistryAuthenticationException) { return Finish(4, "Registry authentication configuration failed."); }
        catch (RegistryHistoryException error)
        {
            var status = error.StatusCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
            return Finish(5, $"Registry history stopped: {error.Failure}; phase {error.Phase}; HTTP {status}.");
        }
        catch (LocalOperationBusyException) { return Finish(7, "Another local registry history operation is active."); }
        catch (Exception) { return Finish(5, "Registry history discovery failed or stopped. No remote changes were submitted."); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, string? error, int years = 0, int courses = 0, int groups = 0)
        {
            if (error is not null) Console.Error.WriteLine(error);
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<object>(1, "registry history", runId,
                    exit == 0 ? "downloaded" : "failed", exit, [], [], [], error is null ? [] : [error], new { years, courses, groups }),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (exit == 0) Console.WriteLine($"Saved local summary: {years} years, {courses} courses, {groups} groups.");
            return exit;
        }
    }
}

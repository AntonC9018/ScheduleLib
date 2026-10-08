using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using CommandDotNet;
using FmiWebsiteInterop.Api;
using FmiWebsiteInterop.Schedule;
using FmiWebsiteInterop.Teachers;
using FmiWebsiteInterop.Theses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing;

namespace ScheduleLib.Cli;

public sealed record WebsiteSchedulesResult(string? OutputDirectory, int FileCount, string? ZipPath, string[] MissingSlugs, string[] OmittedEmpty);
public sealed record WebsiteThesesResult(string? OutputDirectory, int FileCount, string? ZipPath, string[] MissingSlugs);

public partial class ExportCommands
{
    [Command("website-schedules", Description = "Generate per-teacher website schedule JSON files and a ZIP locally for the latest period. Teacher enrichment and slug mapping keep their existing behavior. Nothing is published to the website.")]
    public async Task<int> WebsiteSchedules(SourceArguments source, OutputArguments destination, ResultArguments result, CancellationToken cancellationToken = default, SettingsArguments? settings = null)
    {
        const string command = "export website-schedules";
        const string zipName = "orar.zip";
        var runId = Guid.NewGuid().ToString("N");
        RunOutput? output = null;
        var warnings = new List<string>();
        var missingSlugs = new List<string>();
        var omittedEmpty = new List<string>();
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string? zipPath = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selectedOutput) CliRuntime.ResolvePath(selectedOutput);
            using var resolvedSettings = await CliSettings.Load(settings ?? new(), cancellationToken: cancellation.Token);
            var services = CliRuntime.CreateServices(source, ConfigureServices, resolvedSettings.ProjectDirectory);
            resolvedSettings.ConfigureServices(services);
            output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token, projectDirectory: resolvedSettings.ProjectDirectory);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var full = scope.ServiceProvider.GetRequiredService<Schedule>();
            var grouping = full.TeacherGrouping(FilterHelper.Builder().WithLatestPeriod(full));
            var slugLookup = await scope.ServiceProvider.GetRequiredService<ISlugProvider>().SlugMap(cancellation.Token);
            ValidateWebsiteArtifactNames(slugLookup.Values.Select(x => $"{x}.json"));
            var latest = scope.ServiceProvider.LatestPeriodSchedule();
            var teacherless = latest.EnumerateWeeklyLessons().Count(x => !x.Lesson.Teachers.Any());
            if (teacherless > 0) warnings.Add($"The website schedule export omits {teacherless} weekly lessons with no teacher.");
            var displays = new WebsiteJsonScheduleHelper.Services
            {
                ParityDisplay = new(),
                LessonTypeDisplay = new(),
                SubGroupNumberDisplay = new(),
            };
            foreach (var (teacher, filteredSchedule) in grouping.Filter(full))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var name = new Name(teacher.Item.PersonName.AsNameFields());
                if (!slugLookup.TryGetValue(name, out var slug))
                {
                    missingSlugs.Add(name.ToString());
                    warnings.Add($"No slug for '{name}'.");
                    continue;
                }
                var model = WebsiteJsonScheduleHelper.CreateSerializationModel(filteredSchedule, displays);
                if (model.ScheduleDaysDto.IsEmpty)
                {
                    omittedEmpty.Add(name.ToString());
                    warnings.Add($"Teacher '{name}' has no website schedule entries and is omitted.");
                    continue;
                }
                using var buffer = new MemoryStream();
                await WebsiteJsonScheduleHelper.Serialize(model, buffer);
                if (!files.TryAdd($"{slug}.json", buffer.ToArray()))
                    throw new IOException($"Duplicate website artifact name: {slug}.json");
            }
            foreach (var (name, bytes) in files.OrderBy(x => x.Key, StringComparer.Ordinal))
                await PublishJson(output, name, bytes, cancellation.Token);
            zipPath = await PublishZip(output, zipName, files, cancellation.Token);
            if (files.Count == 0) warnings.Add("No website schedule files were generated.");
            await output.Complete("succeeded", cancellation.Token);
            return await Finish(0, []);
        }
        catch (ArgumentException e) { return await Finish(2, [e.Message]); }
        catch (OperationCanceledException e)
        {
            if (!cancellation.Token.IsCancellationRequested)
                return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]);
            return await Finish(130, ["Cancelled."]);
        }
        catch (LocalOperationBusyException e) { return await Finish(7, [e.Message]); }
        catch (PlatformNotSupportedException e) { return await Finish(8, [e.Message]); }
        catch (JsonException e) { return await Finish(3, [e.Message]); }
        catch (DirectoryNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (InvalidScheduleSourceException e) { return await Finish(3, [e.Message]); }
        catch (ScheduleBuildException e) { return await Finish(3, [e.Message]); }
        catch (FileNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (UnauthorizedAccessException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (IOException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (ItUsmWebsiteHttpException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (HttpRequestException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (Exception e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 1, [e.Message]); }
        finally
        {
            Console.CancelKeyPress -= cancel;
            output?.Dispose();
        }

        async Task<int> Finish(int exit, string[] errors)
        {
            if (exit != 0 && output is not null && output.PublishedPaths.Count > 0
                && !output.PublishedPaths.Contains(output.ManifestPath))
            {
                try { await output.Complete("partial", CancellationToken.None); }
                catch (Exception e) { warnings.Add($"Could not publish partial output manifest: {e.Message}"); }
            }
            var paths = output?.PublishedPaths.ToArray() ?? [];
            foreach (var error in errors) Console.Error.WriteLine(error);
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<WebsiteSchedulesResult>(1, command, runId,
                    exit == 0 ? "succeeded" : paths.Length > 0 ? "partial" : "failed", exit, paths, [], warnings.ToArray(), errors,
                    new(output?.DirectoryPath, files.Count, zipPath,
                        missingSlugs.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                        omittedEmpty.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray())), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                foreach (var path in paths) Console.WriteLine(path);
            return exit;
        }
    }

    [Command("website-theses", Description = "Generate per-teacher website thesis JSON files and a ZIP locally from the configured thesis input. Teacher slug mapping keeps its existing behavior. Nothing is published to the website.")]
    public async Task<int> WebsiteTheses(SourceArguments source, OutputArguments destination, ResultArguments result, CancellationToken cancellationToken = default, SettingsArguments? settings = null)
    {
        const string command = "export website-theses";
        const string zipName = "theses.zip";
        var runId = Guid.NewGuid().ToString("N");
        RunOutput? output = null;
        var warnings = new List<string>();
        var missingSlugs = new List<string>();
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        string? zipPath = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selectedOutput) CliRuntime.ResolvePath(selectedOutput);
            using var resolvedSettings = await CliSettings.Load(settings ?? new(), cancellationToken: cancellation.Token);
            var services = CliRuntime.CreateServices(source, ConfigureServices, resolvedSettings.ProjectDirectory);
            resolvedSettings.ConfigureServices(services);
            var thesisWarnings = new ThesisWarningCollector();
            services.AddSingleton(thesisWarnings);
            services.AddSingleton<ILoggerProvider, ThesisWarningLoggerProvider>();
            output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token, projectDirectory: resolvedSettings.ProjectDirectory);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var slugPreview = await scope.ServiceProvider.GetRequiredService<ItUsmWebsiteTeacherDataProvider>().SlugMap(cancellation.Token);
            ValidateWebsiteArtifactNames(slugPreview.Values.Select(x => $"{x}.json"));
            var stagingRoot = Path.Combine(Path.GetTempPath(), $"schedulelib-theses-{runId}");
            try
            {
                var staging = new OutputDirectory(Path.Combine(stagingRoot, "theses"));
                staging.Initialize(clear: true);
                var handler = scope.ServiceProvider.GetRequiredService<ThesesConversionTaskHandler>();
                await handler.Handle(cancellation.Token, staging);
                foreach (var file in staging.FilePaths("*.json", new() { RecurseSubdirectories = false }).OrderBy(x => x.Path, StringComparer.Ordinal))
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    var stagedName = Path.GetFileName(file.Path);
                    if (!files.TryAdd(stagedName, await File.ReadAllBytesAsync(staging.BuildPath(file.Path), cancellation.Token)))
                        throw new IOException($"Duplicate website artifact name: {stagedName}");
                }
            }
            finally
            {
                if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, recursive: true);
            }
            foreach (var warning in thesisWarnings.Messages)
            {
                missingSlugs.Add(ExtractTeacher(warning));
                warnings.Add(warning);
            }
            ValidateWebsiteArtifactNames(files.Keys);
            foreach (var (name, bytes) in files)
                await PublishJson(output, name, bytes, cancellation.Token);
            zipPath = await PublishZip(output, zipName, files, cancellation.Token);
            if (files.Count == 0) warnings.Add("No website thesis files were generated.");
            await output.Complete("succeeded", cancellation.Token);
            return await Finish(0, []);
        }
        catch (ArgumentException e) { return await Finish(2, [e.Message]); }
        catch (OperationCanceledException e)
        {
            if (!cancellation.Token.IsCancellationRequested)
                return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]);
            return await Finish(130, ["Cancelled."]);
        }
        catch (LocalOperationBusyException e) { return await Finish(7, [e.Message]); }
        catch (PlatformNotSupportedException e) { return await Finish(8, [e.Message]); }
        catch (JsonException e) { return await Finish(3, [e.Message]); }
        catch (DirectoryNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (InvalidScheduleSourceException e) { return await Finish(3, [e.Message]); }
        catch (ScheduleBuildException e) { return await Finish(3, [e.Message]); }
        catch (FileNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (UnauthorizedAccessException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (IOException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (ItUsmWebsiteHttpException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (HttpRequestException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (Exception e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 1, [e.Message]); }
        finally
        {
            Console.CancelKeyPress -= cancel;
            output?.Dispose();
        }

        async Task<int> Finish(int exit, string[] errors)
        {
            if (exit != 0 && output is not null && output.PublishedPaths.Count > 0
                && !output.PublishedPaths.Contains(output.ManifestPath))
            {
                try { await output.Complete("partial", CancellationToken.None); }
                catch (Exception e) { warnings.Add($"Could not publish partial output manifest: {e.Message}"); }
            }
            var paths = output?.PublishedPaths.ToArray() ?? [];
            foreach (var error in errors) Console.Error.WriteLine(error);
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<WebsiteThesesResult>(1, command, runId,
                    exit == 0 ? "succeeded" : paths.Length > 0 ? "partial" : "failed", exit, paths, [], warnings.ToArray(), errors,
                    new(output?.DirectoryPath, files.Count, zipPath,
                        missingSlugs.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray())), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                foreach (var path in paths) Console.WriteLine(path);
            return exit;
        }
    }

    private static void ValidateWebsiteArtifactNames(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or ".."
                || name.Contains('\\') || name.Contains('/') || name.Contains(':')
                || string.Equals(name, "schedulelib-manifest.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ".schedulelib-output.lock", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Invalid website artifact name: {name}");
            if (!seen.Add(name))
                throw new IOException($"Duplicate website artifact name: {name}");
        }
    }

    private static async Task PublishJson(RunOutput output, string name, byte[] bytes, CancellationToken token)
    {
        using var _ = JsonDocument.Parse(bytes);
        await output.Publish(name, async (stream, publishToken) => await stream.WriteAsync(bytes, publishToken), token);
    }

    private static async Task<string> PublishZip(RunOutput output, string zipName, Dictionary<string, byte[]> files, CancellationToken token)
    {
        byte[] zipBytes;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, bytes) in files.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes, token);
                }
            }
            zipBytes = buffer.ToArray();
        }
        await output.Publish(zipName, async (stream, publishToken) =>
        {
            await stream.WriteAsync(zipBytes, publishToken);
            publishToken.ThrowIfCancellationRequested();
        }, token);
        var zipPath = Path.Combine(output.DirectoryPath, zipName);
        VerifyZip(zipPath, files.Keys);
        return zipPath;
    }

    private static void VerifyZip(string zipPath, IEnumerable<string> expectedNames)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var actual = zip.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.FullName).ToHashSet(StringComparer.Ordinal);
        var expected = expectedNames.ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected))
        {
            var missing = expected.Except(actual, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var extra = actual.Except(expected, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            throw new InvalidDataException($"ZIP content mismatch in {zipPath}: missing [{string.Join(", ", missing)}], extra [{string.Join(", ", extra)}].");
        }
    }

    private static string ExtractTeacher(string warning)
    {
        var first = warning.IndexOf('\'');
        var last = warning.LastIndexOf('\'');
        return first >= 0 && last > first ? warning[(first + 1)..last] : warning;
    }

    private sealed class ThesisWarningCollector
    {
        public List<string> Messages { get; } = [];
    }

    private sealed class ThesisWarningLoggerProvider(ThesisWarningCollector collector) : ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
            categoryName.Contains("ThesesConversion", StringComparison.Ordinal)
                ? new ThesisWarningLogger(collector)
                : new NullWebsiteLogger();

        public void Dispose() { }
    }

    private sealed class ThesisWarningLogger(ThesisWarningCollector collector) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullWebsiteScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            lock (collector) collector.Messages.Add(formatter(state, exception));
        }
    }

    private sealed class NullWebsiteLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullWebsiteScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private sealed class NullWebsiteScope : IDisposable
    {
        public static readonly NullWebsiteScope Instance = new();

        public void Dispose() { }
    }
}

using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using Xunit;
using Event = Google.Apis.Calendar.v3.Data.Event;

public sealed class CliJsonlTests
{
    private static JsonSerializerOptions JsonOptions => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Fact]
    public async Task JsonAndJsonlTogetherRejectWithArgumentError()
    {
        await using var fake = new FakeProvider();
        var commands = new TestCommands(fake);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(2, await commands.Sync(new(), new() { Profile = "synthetic-teacher" },
                new() { Json = true, Jsonl = true }, new()));
            Assert.Empty(stdout.ToString());
            Assert.Contains("Only one of --json and --jsonl", stderr.ToString());
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }

        // CliHost routing also rejects the pair.
        Assert.Equal(2, await CliHost.Run(["query", "lessons", "--json", "--jsonl"]));
    }

    [Fact]
    public async Task BlockedApplyStreamsEarlyItemsBeforeFinal()
    {
        var block = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fake = new FakeProvider { BlockSecond = block.Task };
        var commands = new TestCommands(fake);
        using var stdoutCapture = new SyncCapture();
        using var stderrCapture = new SyncCapture();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(stdoutCapture);
            Console.SetError(stderrCapture);
            var run = commands.Sync(new(), new() { Profile = "synthetic-teacher" },
                new() { Jsonl = true }, new() { Apply = true });
            // Wait for early output: start + 3 planned/not-attempted + first completed.
            var early = await WaitFor(() => stdoutCapture.Snapshot(), s => s.Contains("\"event\":\"start\"")
                && s.Contains("\"id\":\"0\"") && s.Contains("completed") && !s.Contains("\"event\":\"final\""), TimeSpan.FromSeconds(5));
            Assert.Contains("\"event\":\"start\"", early);
            Assert.Contains("\"id\":\"0\"", early);
            Assert.DoesNotContain("\"event\":\"final\"", early);
            block.SetResult(true);
            Assert.Equal(0, await run);
            var all = stdoutCapture.Snapshot();
            var lines = all.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 1 + 3 + 1 + 1, $"Expected start + initial + updates + final, got {lines.Length}");
            var final = JsonDocument.Parse(lines[^1]).RootElement;
            Assert.Equal("final", final.GetProperty("event").GetString());
            Assert.Equal("applied", final.GetProperty("status").GetString());
            // Final must not repeat the full outcomes array.
            Assert.False(final.TryGetProperty("outcomes", out _), "Final JSONL must not repeat emitted items.");
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    [Fact]
    public async Task JsonlSchemaSequenceAndRunCorrelation()
    {
        await using var fake = new FakeProvider();
        var (exit, stdout, stderr) = await CaptureJsonl(
            () => new TestCommands(fake).Sync(new(), new() { Profile = "synthetic-teacher" }, new() { Jsonl = true }, new()));
        Assert.Equal(0, exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 3);
        string? runId = null;
        for (var i = 0; i < lines.Length; i++)
        {
            using var doc = JsonDocument.Parse(lines[i]);
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("registry sync", root.GetProperty("command").GetString());
            Assert.Equal(i, root.GetProperty("seq").GetInt32());
            runId = runId is null ? root.GetProperty("runId").GetString() : runId;
            Assert.Equal(runId, root.GetProperty("runId").GetString());
        }
        Assert.Equal("start", JsonDocument.Parse(lines[0]).RootElement.GetProperty("event").GetString());
        Assert.Equal("final", JsonDocument.Parse(lines[^1]).RootElement.GetProperty("event").GetString());
        // Stable identity: each action id appears, preview emits planned once.
        var ids = lines.Skip(1).Take(lines.Length - 2)
            .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("id").GetString()).ToArray();
        Assert.Equal(new[] { "0", "1", "2" }, ids);
        // Stderr warnings are JSONL with independent seq starting at 0 and same runId.
        var errLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(errLines);
        for (var i = 0; i < errLines.Length; i++)
        {
            using var doc = JsonDocument.Parse(errLines[i]);
            var root = doc.RootElement;
            Assert.Equal("log", root.GetProperty("event").GetString());
            Assert.Equal(i, root.GetProperty("seq").GetInt32());
            Assert.Equal(runId, root.GetProperty("runId").GetString());
        }
        // Stdout contains no human text.
        foreach (var line in lines) Assert.StartsWith("{", line.TrimStart());
    }

    [Fact]
    public void StructuredLogsStayOnStderrWithoutStdoutContamination()
    {
        var output = new CliOutput("synthetic command", "synthetic-run", false, true);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            using var provider = new CliJsonlLoggerProvider(output);
            var logger = provider.CreateLogger("synthetic");
            logger.LogInformation("Synthetic information {Value}", 42);
            logger.LogWarning("Synthetic warning");
            logger.LogError(new InvalidOperationException("synthetic failure"), "Synthetic error");
            output.WriteWarning("Synthetic warning line");
            output.WriteError("Synthetic error line");
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
        Assert.Empty(stdout.ToString());
        var lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, lines.Length);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("synthetic-run", root.GetProperty("runId").GetString());
            Assert.Equal("log", root.GetProperty("event").GetString());
        }
        Assert.Contains("Information", lines[0]);
        Assert.Contains("Warning", lines[1]);
        Assert.Contains("Error", lines[2]);
    }

    [Fact]
    public async Task PlanLogsMatchUnderPreviewAndApply()
    {
        var previewLogs = await PlanLogs(explicitApply: false);
        var applyLogs = await PlanLogs(explicitApply: true);
        Assert.NotEmpty(previewLogs);
        Assert.Equal(previewLogs, applyLogs);

        static async Task<string[]> PlanLogs(bool explicitApply)
        {
            using var settings = await CliSettings.Load(new() { Profile = "Curmanschii Anton" });
            var services = CliRuntime.CreateServices(new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") }, AppConfiguration.ConfigureServices);
            settings.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
            using var capture = new LogCapture();
            services.AddLogging(b => { b.ClearProviders(); b.AddProvider(capture); });
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(default);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            var lesson = schedule.EnumerateWeeklyLessons().First();
            var navigator = new FixtureNavigator(lesson.Lesson.Course, new GroupLink(new FoundGroups
            {
                IsWildcard = true, Value = lesson.Lesson.Groups,
            }, lesson.Lesson.GroupPartitionKey, new("https://registry.test/group"), new("https://registry.test/evaluation")));
            var handler = scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>();
            await handler.Plan(new()
            {
                Navigator = navigator, Attendance = new([]), LessonTopics = NoLessonTopics.Instance, Semester = Semester.Sem2,
            }, explicitApply: explicitApply);
            return capture.Messages.ToArray();
        }
    }

    [Fact]
    public async Task EarlyArgumentFailuresEmitJsonlStartAndFinal()
    {
        // Missing profile fails before any provider work.
        var (exit, stdout, stderr) = await CaptureJsonl(
            () => new TestCommands(new FakeProvider()).Sync(new(), new(), new() { Jsonl = true }, new()));
        Assert.Equal(3, exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("start", JsonDocument.Parse(lines[0]).RootElement.GetProperty("event").GetString());
        var final = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Equal("final", final.GetProperty("event").GetString());
        Assert.Equal("failed", final.GetProperty("status").GetString());
        Assert.Equal(3, final.GetProperty("exitCode").GetInt32());
        Assert.NotEmpty(final.GetProperty("errors").EnumerateArray());
        using var err = JsonDocument.Parse(stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        Assert.Equal("log", err.RootElement.GetProperty("event").GetString());

        // Invalid quiz id is an argument error before planning.
        var (gradeExit, gradeStdout, _) = await CaptureJsonl(
            () => new TestGradeCommands(new FakeGradeProvider()).ImportGrades(new() { QuizId = "0" }, new(), new() { Profile = "synthetic-teacher" }, new() { Jsonl = true }, new()));
        Assert.Equal(2, gradeExit);
        Assert.Equal("final", JsonDocument.Parse(gradeStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1]).RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public async Task StartupParseErrorWithJsonlEmitsJsonlOnBothStreams()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(2, await CliHost.Run(["query", "lessons", "--unknown-flag", "--jsonl"]));
            var outLines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, outLines.Length);
            Assert.Equal("start", JsonDocument.Parse(outLines[0]).RootElement.GetProperty("event").GetString());
            Assert.Equal("final", JsonDocument.Parse(outLines[1]).RootElement.GetProperty("event").GetString());
            var errLines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.NotEmpty(errLines);
            foreach (var line in errLines) JsonDocument.Parse(line);
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    [Fact]
    public async Task CancellationAndUncertainActionsStreamWithCancelledFinal()
    {
        await using var fake = new FakeProvider { CancelSecond = true };
        var (exit, stdout, _) = await CaptureJsonl(
            () => new TestCommands(fake).Sync(new(), new() { Profile = "synthetic-teacher" }, new() { Jsonl = true }, new() { Apply = true }));
        Assert.Equal(130, exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1).Take(lines.Length - 2).Select(l => JsonDocument.Parse(l)).Select(d => d.RootElement).ToArray();
        Assert.Contains(items, x => x.GetProperty("id").GetString() == "0" && x.GetProperty("status").GetString() == "completed");
        Assert.Contains(items, x => x.GetProperty("id").GetString() == "1" && x.GetProperty("status").GetString() == "uncertain");
        var final = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal("cancelled", final.GetProperty("status").GetString());
        Assert.Equal(130, final.GetProperty("exitCode").GetInt32());
        Assert.False(final.TryGetProperty("outcomes", out _));

        await using var failing = new FakeProvider { ActionFailure = new RegistryActionExecutionException(true, new IOException("synthetic lost response")) };
        var (failExit, failStdout, _) = await CaptureJsonl(
            () => new TestCommands(failing).Sync(new(), new() { Profile = "synthetic-teacher" }, new() { Jsonl = true }, new() { Apply = true }));
        Assert.Equal(6, failExit);
        var failFinal = JsonDocument.Parse(failStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1]).RootElement;
        Assert.Equal("partial", failFinal.GetProperty("status").GetString());
    }

    [Fact]
    public async Task JsonRemainsSingleCompleteEnvelope()
    {
        await using var fake = new FakeProvider();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(0, await new TestCommands(fake).Sync(new(), new() { Profile = "synthetic-teacher" }, new() { Json = true }, new()));
            var text = stdout.ToString().Trim();
            Assert.StartsWith("{", text);
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("registry sync", doc.RootElement.GetProperty("command").GetString());
            Assert.Equal("preview", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal(3, doc.RootElement.GetProperty("data").GetProperty("outcomes").GetArrayLength());
            // Plain --json keeps existing stderr format (human warning text, not JSONL).
            Assert.Contains("positional", stderr.ToString());
            Assert.DoesNotContain("\"event\":\"log\"", stderr.ToString());
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    [Fact]
    public async Task ConcurrentWritesKeepPerStreamOrderedSequence()
    {
        var output = new CliOutput("synthetic command", "synthetic-run", false, true);
        using var stdout = new SyncCapture();
        using var stderr = new SyncCapture();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            output.EmitStart();
            var writers = Enumerable.Range(0, 8).Select((worker) => Task.Run(() =>
            {
                for (var i = 0; i < 50; i++)
                {
                    output.EmitItem($"w{worker}-i{i}", "completed", new { worker, i });
                    output.WriteWarning($"synthetic warning w{worker} i{i}");
                }
            })).ToArray();
            await Task.WhenAll(writers);
            output.EmitFinal("succeeded", 0, [], [], new { count = 400 });
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
        // Allocation, serialization, write and flush hold one lock per record,
        // so file order matches sequence order on each independent stream.
        var outLines = stdout.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 400 + 1, outLines.Length);
        for (var i = 0; i < outLines.Length; i++)
        {
            using var doc = JsonDocument.Parse(outLines[i]);
            var root = doc.RootElement;
            Assert.Equal(i, root.GetProperty("seq").GetInt32());
            Assert.Equal("synthetic-run", root.GetProperty("runId").GetString());
        }
        Assert.Equal("start", JsonDocument.Parse(outLines[0]).RootElement.GetProperty("event").GetString());
        Assert.Equal("final", JsonDocument.Parse(outLines[^1]).RootElement.GetProperty("event").GetString());
        var errLines = stderr.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(400, errLines.Length);
        for (var i = 0; i < errLines.Length; i++)
        {
            using var doc = JsonDocument.Parse(errLines[i]);
            Assert.Equal(i, doc.RootElement.GetProperty("seq").GetInt32());
        }
    }

    [Fact]
    public void StructuredLogsPreserveContentLevelsPropertiesScopesExceptionAndFiltering()
    {
        var output = new CliOutput("synthetic command", "synthetic-run", false, true);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            using var provider = new CliJsonlLoggerProvider(output);
            var logger = provider.CreateLogger("synthetic");
            logger.LogDebug("Synthetic debug {Value}", 1);
            using (logger.BeginScope("synthetic-scope"))
                logger.LogInformation("Synthetic information {Value}", 42);
            logger.LogError(new InvalidOperationException("synthetic failure"), "Synthetic error");
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
        Assert.Empty(stdout.ToString());
        // Debug never reached stderr before and stays filtered; the existing
        // Information/Error content, levels and occurrence are preserved.
        var lines = stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var info = JsonDocument.Parse(lines[0]);
        Assert.Equal("Information", info.RootElement.GetProperty("level").GetString());
        Assert.Equal("Synthetic information 42", info.RootElement.GetProperty("message").GetString());
        Assert.Equal(42, info.RootElement.GetProperty("properties").GetProperty("Value").GetInt32());
        Assert.Contains("synthetic-scope", info.RootElement.GetProperty("scopes").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("synthetic", info.RootElement.GetProperty("category").GetString());
        Assert.True(info.RootElement.TryGetProperty("timestamp", out var timestamp)
            && DateTimeOffset.TryParse(timestamp.GetString(), out _));
        using var error = JsonDocument.Parse(lines[1]);
        Assert.Equal("Error", error.RootElement.GetProperty("level").GetString());
        Assert.Contains("System.InvalidOperationException: synthetic failure", error.RootElement.GetProperty("exception").GetString());
    }

    [Fact]
    public async Task ConcurrentLogScopesStayWithinTheirAsyncExecution()
    {
        using var stderr = new SyncCapture();
        var originalError = Console.Error;
        Console.SetError(stderr);
        try
        {
            using var provider = new CliJsonlLoggerProvider(new CliOutput("synthetic", "scope-run", false, true));
            var logger = provider.CreateLogger("synthetic");
            using var parent = logger.BeginScope(new Dictionary<string, object?> { ["Parent"] = "shared" });
            var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var count = 0;
            await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
            {
                using var child = logger.BeginScope("child-" + index);
                if (Interlocked.Increment(ref count) == 2) bothStarted.SetResult();
                await bothStarted.Task;
                logger.LogInformation("Child {Index}", index);
            })));
            logger.LogInformation("Parent");
        }
        finally { Console.SetError(originalError); }
        var lines = stderr.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        foreach (var line in lines.Take(2))
        {
            using var record = JsonDocument.Parse(line);
            var properties = record.RootElement.GetProperty("properties");
            Assert.Equal("shared", properties.GetProperty("Parent").GetString());
            var scopes = record.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()).ToArray();
            Assert.Equal(2, scopes.Length);
            Assert.Contains("child-" + properties.GetProperty("Index").GetInt32(), scopes);
        }
        using var last = JsonDocument.Parse(lines[^1]);
        Assert.Single(last.RootElement.GetProperty("scopes").EnumerateArray());
    }

    [Fact]
    public async Task HumanProfileListingEndsWithSummary()
    {
        using var isolation = new XdgIsolation();
        var originalOut = Console.Out;
        using var stdout = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Assert.Equal(0, await new ConfigCommands().Profiles(new(), new()));
        }
        finally { Console.SetOut(originalOut); }
        var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.Equal($"succeeded: {lines.Length - 1} profiles.", lines[^1].TrimEnd());
        Assert.Single(lines, line => line.StartsWith("succeeded:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryLessonsPartialCancellationKeepsStreamedCount()
    {
        using var cancelled = new CancellationTokenSource();
        using var stdout = new CancellingWriter(cancelled, 3);
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        int exit;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exit = await new FixtureQuery().Lessons(new(),
                new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") },
                new() { Jsonl = true }, cancelled.Token);
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
        Assert.Equal(130, exit);
        var lines = stdout.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1).Take(lines.Length - 2).ToArray();
        var final = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal("cancelled", final.GetProperty("status").GetString());
        Assert.Equal(130, final.GetProperty("exitCode").GetInt32());
        // Counts persist accurately: the summary count matches emitted rows,
        // never zero when rows already streamed and never the full set.
        Assert.Equal(items.Length, final.GetProperty("data").GetProperty("count").GetInt32());
        Assert.DoesNotContain("lessons", final.GetProperty("data").EnumerateObject().Select(x => x.Name));
    }

    [Fact]
    public async Task CalendarCancellationReportsRemainingActionsWithoutChangingLegacyOutcomes()
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = new CalendarFake();
        using var root = new TempRoot();
        var updates = new List<(string Id, string Status, object? Item, string Human)>();
        var result = await CalendarSync.Run(provider, "Lessons", [new Event { Summary = "Desired" }], true,
            cancellation.Token, lockRoot: root.Path, onOutcome: update =>
            {
                updates.Add(update);
                if (update.Id == "0" && update.Status == "completed") cancellation.Cancel();
            });
        Assert.Equal(130, result.ExitCode);
        Assert.Single(result.Outcomes);
        Assert.Equal("completed", result.Outcomes[0].State);
        Assert.Equal(["delete old"], provider.Writes);
        Assert.Equal(["1", "2"], updates.Where(update => update.Status == "not-attempted").Select(update => update.Id));
    }

    [Fact]
    public async Task CalendarPlanIdsAreStableAcrossPreviewApplyAndTransitions()
    {
        var desired = new[] { new Event { Summary = "Desired A" }, new Event { Summary = "Desired B" } };
        var previewUpdates = new List<(string Id, string Status, object? Item, string Human)>();
        using var previewProvider = new CalendarFake();
        using var previewLock = new TempRoot();
        var preview = await CalendarSync.Run(previewProvider, "Lessons", desired, false, default, lockRoot: previewLock.Path,
            onOutcome: (update) => previewUpdates.Add(update));
        Assert.Empty(preview.Outcomes);
        Assert.Empty(previewProvider.Writes);
        // Planned action ids are plan-order indexes; the read selection
        // streams as items with the deleted/desired payloads.
        Assert.Equal(["0", "1", "2", "3"], previewUpdates.Where((update) => update.Status == "planned").Select((update) => update.Id));
        Assert.Equal("planned", previewUpdates.Single((update) => update.Id == "0").Status);
        var deleted = previewUpdates.Single((update) => update.Id == "deleted-0");
        Assert.Equal("Old", JsonDocument.Parse(JsonSerializer.Serialize(deleted.Item, JsonOptions)).RootElement.GetProperty("summary").GetString());
        Assert.Equal(["Desired A", "Desired B"], previewUpdates
            .Where((update) => update.Id.StartsWith("desired-", StringComparison.Ordinal)).OrderBy((update) => update.Id)
            .Select((update) => JsonDocument.Parse(JsonSerializer.Serialize(update.Item, JsonOptions)).RootElement.GetProperty("summary").GetString()));

        var applyUpdates = new List<(string Id, string Status, object? Item, string Human)>();
        using var applyProvider = new CalendarFake();
        using var applyLock = new TempRoot();
        var applied = await CalendarSync.Run(applyProvider, "Lessons", desired, true, default, lockRoot: applyLock.Path,
            onOutcome: (update) => applyUpdates.Add(update));
        Assert.Equal(0, applied.ExitCode);
        Assert.Equal(["delete old", "calendar", "event new", "event new"], applyProvider.Writes);
        Assert.All(applied.Outcomes, (outcome) => Assert.Equal("completed", outcome.State));
        // Each action id transitions from planned to completed with the same id.
        foreach (var id in new[] { "0", "1", "2", "3" })
            Assert.Equal(["planned", "completed"], applyUpdates.Where((update) => update.Id == id).Select((update) => update.Status));
        Assert.Equal("new", applied.Outcomes[1].CalendarId);
        Assert.Equal("event-1", applied.Outcomes[2].EventId);
        Assert.Equal(0, applied.Outcomes[2].DesiredEventIndex);
    }

    [Fact]
    public async Task CalendarThrowingCallbackPreservesCompletedOutcomeWithoutRetry()
    {
        using var provider = new CalendarFake();
        using var calendarLock = new TempRoot();
        var calls = 0;
        var failure = Assert.Throws<CalendarStreamingException>(() => CalendarSync.Run(provider, "Lessons",
            [new Event { Summary = "Desired" }], true, default, lockRoot: calendarLock.Path,
            onOutcome: (_) =>
            {
                // Fail on the first terminal transition (after 3 planned plus
                // 2 selection reports): the delete already completed remotely.
                if (++calls == 6) throw new InvalidOperationException("synthetic render failure");
            }).GetAwaiter().GetResult());
        Assert.IsAssignableFrom<CliOutputStreamingException>(failure);
        // Rendering stopped further mutations without retrying or reclassifying.
        Assert.Equal(["delete old"], provider.Writes);
        var outcome = Assert.Single(failure.PartialResult.Outcomes);
        Assert.Equal("completed", outcome.State);
        Assert.Equal("old", outcome.CalendarId);
        Assert.Equal(6, failure.PartialResult.ExitCode);
    }

    [Fact]
    public async Task DrivePlanIdsAreStableAcrossPreviewApplyAndTransitions()
    {
        using var root = new TempRoot();
        var artifacts = new[] { new DriveArtifact("a.pdf", Path.Combine(root.Path, "a.pdf")) };
        var previewUpdates = new List<(string Id, string Status, object? Item, string Human)>();
        using var previewProvider = new DriveFake();
        var preview = await DriveSync.Run(previewProvider, "Configured", artifacts, false, default, root.Path,
            onOutcome: (update) => previewUpdates.Add(update));
        Assert.Empty(preview.Outcomes);
        Assert.Empty(previewProvider.Writes);
        Assert.Equal(["0", "1"], previewUpdates.Select((update) => update.Id));
        Assert.All(previewUpdates, (update) => Assert.Equal("planned", update.Status));
        Assert.Equal("delete", ((DriveOutcome)previewUpdates[0].Item!).Action.Action);
        Assert.Equal("create", ((DriveOutcome)previewUpdates[1].Item!).Action.Action);

        var applyUpdates = new List<(string Id, string Status, object? Item, string Human)>();
        using var applyProvider = new DriveFake();
        await using var created = File.Create(Path.Combine(root.Path, "a.pdf"));
        await created.WriteAsync("fixture bytes"u8.ToArray());
        await created.FlushAsync();
        await created.DisposeAsync();
        var applied = await DriveSync.Run(applyProvider, "Configured", artifacts, true, default, root.Path,
            onOutcome: (update) => applyUpdates.Add(update));
        Assert.Equal(0, applied.ExitCode);
        Assert.Equal(["delete old", "create a.pdf"], applyProvider.Writes);
        foreach (var id in new[] { "0", "1" })
            Assert.Equal(["planned", "completed"], applyUpdates.Where((update) => update.Id == id).Select((update) => update.Status));
    }

    [Fact]
    public async Task DriveThrowingCallbackPreservesCompletedOutcomeWithoutRetry()
    {
        using var root = new TempRoot();
        using var provider = new DriveFake();
        var artifacts = new[] { new DriveArtifact("a.pdf", Path.Combine(root.Path, "a.pdf")) };
        var calls = 0;
        var failure = await Assert.ThrowsAsync<DriveStreamingException>(() => DriveSync.Run(provider, "Configured",
            artifacts, true, default, root.Path, onOutcome: (_) =>
            {
                // Fail on the first terminal transition (after 2 planned
                // reports): the delete already completed remotely.
                if (++calls == 3) throw new InvalidOperationException("synthetic render failure");
            }));
        Assert.IsAssignableFrom<CliOutputStreamingException>(failure);
        Assert.Equal(["delete old"], provider.Writes);
        var outcome = Assert.Single(failure.PartialResult.Outcomes);
        Assert.Equal("completed", outcome.State);
        Assert.Equal("old", outcome.ResultFileId);
        Assert.Equal(6, failure.PartialResult.ExitCode);
    }

    [Fact]
    public async Task RegistryApplyStopsAfterRenderingFailureWithPartialCounts()
    {
        await using var fake = new FakeProvider();
        var commands = new TestCommands(fake);
        // Fail exactly one console write mid-apply, then recover so the final
        // summary still renders and the exit code survives.
        using var stdout = new FlakyWriter(6);
        using var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        int exit;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exit = await commands.Sync(new(), new() { Profile = "synthetic-teacher" },
                new() { Jsonl = true }, new() { Apply = true });
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
        Assert.Equal(6, exit);
        Assert.Equal([0, 1], fake.Executed.OrderBy((index) => index));
        var lines = stdout.Snapshot().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var final = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal("partial", final.GetProperty("status").GetString());
        Assert.Equal(6, final.GetProperty("exitCode").GetInt32());
        Assert.Contains("Output streaming failed", final.GetProperty("errors").EnumerateArray().First().GetString());
        var data = final.GetProperty("data");
        Assert.Equal(3, data.GetProperty("total").GetInt32());
        // Both executed actions keep their completed remote outcomes even
        // though the second one's rendering failed; nothing was retried.
        Assert.Equal(2, data.GetProperty("counts").GetProperty("completed").GetInt32());
        Assert.False(final.TryGetProperty("outcomes", out _));
    }

    [Fact]
    public async Task FinalSummariesUseCountsWithoutStreamedArrays()
    {
        await using var fake = new FakeProvider();
        var (exit, stdout, _) = await CaptureJsonl(
            () => new TestCommands(fake).Sync(new(), new() { Profile = "synthetic-teacher" }, new() { Jsonl = true }, new()));
        Assert.Equal(0, exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal(3, final.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Equal(3, final.GetProperty("data").GetProperty("counts").GetProperty("planned").GetInt32());
        Assert.DoesNotContain("\"outcomes\"", finalLine);

        var (queryExit, queryStdout, _) = await CaptureJsonl(() => new FixtureQuery().Lessons(new(),
            new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") }, new() { Jsonl = true }, default));
        Assert.Equal(0, queryExit);
        var queryLines = queryStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var queryFinal = JsonDocument.Parse(queryLines[^1]).RootElement;
        Assert.Equal(queryLines.Length - 2, queryFinal.GetProperty("data").GetProperty("count").GetInt32());
        Assert.Single(queryFinal.GetProperty("data").EnumerateObject());

        var historyDirectory = NewHistoryDirectory();
        try
        {
            var (historyExit, historyStdout, _) = await CaptureJsonl(() => new HistoryCommands(new HistoryProvider()).History(
                new() { Profile = "synthetic" }, new() { LocalDirectory = historyDirectory }, new() { Jsonl = true }));
            Assert.Equal(0, historyExit);
            var historyLines = historyStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(1 + 1 + 1, historyLines.Length);
            var historyFinal = JsonDocument.Parse(historyLines[^1]).RootElement;
            Assert.Equal(1, historyFinal.GetProperty("data").GetProperty("years").GetInt32());
            Assert.Equal(["courses", "groups", "years"], historyFinal.GetProperty("data").EnumerateObject().Select((property) => property.Name).OrderBy((name) => name));
            Assert.DoesNotContain("private-course", historyStdout);
        }
        finally { Directory.Delete(Path.GetDirectoryName(historyDirectory)!, recursive: true); }
    }

    [Fact]
    public async Task CalendarCommandPreviewStreamsSelectionWithScalarFinal()
    {
        using var fake = new CalendarFake();
        var (exit, stdout, _) = await CaptureJsonl(() => new FixtureCalendar(fake).Sync(
            new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") },
            new() { Profile = "Nartea Nichita" }, new() { Jsonl = true }, new()));
        Assert.Equal(0, exit);
        Assert.Empty(fake.Writes);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1).Take(lines.Length - 2)
            .Select((line) => JsonDocument.Parse(line).RootElement).ToArray();
        // Planned action ids are plan-order indexes shared with apply mode.
        // (The fixture's configured calendar name differs from the fake's
        // existing calendar, so this preview has no delete step; the
        // sync-layer test above covers deleted-event selection items.)
        var planned = items.Where((item) => item.GetProperty("status").GetString() == "planned").ToArray();
        Assert.Equal(Enumerable.Range(0, planned.Length).Select((index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture)), planned.Select((item) => item.GetProperty("id").GetString()));
        Assert.Contains(items, (item) => item.GetProperty("id").GetString()?.StartsWith("desired-", StringComparison.Ordinal) == true);
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal("preview", final.GetProperty("status").GetString());
        Assert.True(final.GetProperty("data").GetProperty("desiredCount").GetInt32() > 0);
        Assert.DoesNotContain("\"desiredEvents\"", finalLine);
        Assert.DoesNotContain("\"deletedEvents\"", finalLine);
        Assert.DoesNotContain("\"outcomes\"", finalLine);
    }

    [Fact]
    public async Task DriveCommandPreviewStreamsPlanWithScalarFinal()
    {
        using var output = new TempRoot();
        using var fake = new DriveFake();
        var (exit, stdout, _) = await CaptureJsonl(() => new FixtureDrive(fake).Publish(
            new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") },
            new() { Profile = "Curmanschii Anton" }, new() { Directory = output.Path }, new() { Jsonl = true }, new()));
        Assert.Equal(0, exit);
        Assert.Empty(fake.Writes);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1).Take(lines.Length - 2)
            .Select((line) => JsonDocument.Parse(line).RootElement).ToArray();
        Assert.Contains(items, (item) => item.GetProperty("id").GetString()?.StartsWith("artifact:", StringComparison.Ordinal) == true);
        var planned = items.Where((item) => item.GetProperty("status").GetString() == "planned").ToArray();
        Assert.NotEmpty(planned);
        Assert.Equal(Enumerable.Range(0, planned.Length).Select((index) => index.ToString(System.Globalization.CultureInfo.InvariantCulture)), planned.Select((item) => item.GetProperty("id").GetString()));
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal("preview", final.GetProperty("status").GetString());
        Assert.Equal(planned.Length, final.GetProperty("data").GetProperty("actionCount").GetInt32());
        Assert.DoesNotContain("\"actions\"", finalLine);
        Assert.DoesNotContain("\"outcomes\"", finalLine);
    }

    [Fact]
    public async Task ConfigProfilesAndAuthStatusStreamCollectionsWithScalarFinals()
    {
        using var isolation = new XdgIsolation();
        var (exit, stdout, _) = await CaptureJsonl(() => new ConfigCommands().Profiles(new(), new() { Jsonl = true }));
        Assert.Equal(0, exit);
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var items = lines.Skip(1).Take(lines.Length - 2).ToArray();
        Assert.NotEmpty(items);
        foreach (var line in items)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("completed", doc.RootElement.GetProperty("status").GetString());
            Assert.NotEmpty(doc.RootElement.GetProperty("item").GetProperty("profile").GetString()!);
        }
        var finalLine = lines[^1];
        var final = JsonDocument.Parse(finalLine).RootElement;
        Assert.Equal(items.Length, final.GetProperty("data").GetProperty("profileCount").GetInt32());
        Assert.DoesNotContain("\"profiles\"", finalLine);

        var (authExit, authStdout, _) = await CaptureJsonl(() => new AuthCommands().Status(
            new() { Provider = AuthProvider.google }, new() { Profile = "Curmanschii Anton" }, new() { Jsonl = true }));
        Assert.Equal(0, authExit);
        var authLines = authStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var authFinal = JsonDocument.Parse(authLines[^1]).RootElement;
        Assert.Equal("succeeded", authFinal.GetProperty("status").GetString());
        Assert.Equal(0, authFinal.GetProperty("data").GetProperty("accountCount").GetInt32());
        Assert.False(authFinal.GetProperty("data").GetProperty("provisioned").GetBoolean());
        Assert.DoesNotContain("\"accounts\"", authLines[^1]);
    }

    [Fact]
    public async Task HistoryTopicsStreamsCollectionsWithScalarFinal()
    {
        var directory = NewHistoryDirectory();
        try
        {
            var (exit, stdout, _) = await CaptureJsonl(() => new HistoryTopicsCommands(new HistoryTopicsProvider()).HistoryTopics(
                new() { Profile = "synthetic" }, new() { LocalDirectory = directory }, new() { Jsonl = true }));
            Assert.Equal(0, exit);
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var items = lines.Skip(1).Take(lines.Length - 2)
                .Select((line) => JsonDocument.Parse(line).RootElement).ToArray();
            Assert.Equal(2, items.Count((item) => item.GetProperty("id").GetString()?.StartsWith("year-", StringComparison.Ordinal) == true));
            Assert.Equal(1, items.Count((item) => item.GetProperty("id").GetString()?.StartsWith("candidate-", StringComparison.Ordinal) == true));
            var final = JsonDocument.Parse(lines[^1]).RootElement;
            Assert.Equal(1, final.GetProperty("data").GetProperty("courses").GetInt32());
            Assert.Equal(1, final.GetProperty("data").GetProperty("candidates").GetInt32());
            Assert.Equal(0, final.GetProperty("data").GetProperty("unresolved").GetInt32());
            Assert.DoesNotContain("canary-course", stdout);
            Assert.DoesNotContain("canary-group", stdout);
            Assert.DoesNotContain("canary-historical-topic", stdout);
        }
        finally { Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true); }
    }

    [Fact]
    public async Task HumanSummariesDoNotDuplicateStreamedItems()
    {
        await using var fake = new FakeProvider();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(0, await new TestCommands(fake).Sync(new(), new() { Profile = "synthetic-teacher" }, new(), new()));
            var lines = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Count((line) => line.Contains(": planned")));
            Assert.Single(lines, (line) => line.StartsWith("preview:", StringComparison.Ordinal));
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    [Fact]
    public async Task EveryCommandRejectsJsonTogetherWithJsonl()
    {
        var both = new ResultArguments { Json = true, Jsonl = true };
        var cases = new (string Name, Func<Task<int>> Run)[]
        {
            ("query lessons", () => new QueryCommands().Lessons(new(), new(), both, default)),
            ("query free-hours", () => new QueryCommands().FreeHours(new(), new(), both, default)),
            ("export teachers-excel", () => new ExportCommands().TeachersExcel(new(), new(), both, default)),
            ("export free-rooms", () => new ExportCommands().FreeRooms(new(), new(), both, default)),
            ("export pdf", () => new ExportCommands().Pdf(new(), new(), both, default)),
            ("export ics", () => new ExportCommands().Ics(new(), new(), both, default)),
            ("export lab-deadlines", () => new ExportCommands().LabDeadlines(new(), new(), both, new(), default)),
            ("export pre-defense", () => new ExportCommands().PreDefense(new(), both, new(), default)),
            ("export website-schedules", () => new ExportCommands().WebsiteSchedules(new(), new(), both, default)),
            ("export website-theses", () => new ExportCommands().WebsiteTheses(new(), new(), both, default)),
            ("curricula download", () => new CurriculaCommands().Download(new(), new(), both, default)),
            ("config show", () => new ConfigCommands().Show(new(), new(), both, default)),
            ("config get", () => new ConfigCommands().Get(new() { Key = "key" }, new(), new(), both, default)),
            ("config validate", () => new ConfigCommands().Validate(new(), both, default)),
            ("config profiles", () => new ConfigCommands().Profiles(new(), both, default)),
            ("config set", () => new ConfigCommands().Set(new() { Key = "key", Value = "1" }, new() { Scope = "user" }, new(), both, default)),
            ("config unset", () => new ConfigCommands().Unset(new() { Key = "key" }, new() { Scope = "user" }, new(), both, default)),
            ("auth login google", () => new AuthCommands().Login(new() { Provider = AuthProvider.google }, new(), both, default)),
            ("auth status google", () => new AuthCommands().Status(new() { Provider = AuthProvider.google }, new(), both, default)),
            ("auth logout google", () => new AuthCommands().Logout(new() { Provider = AuthProvider.google }, new(), both, default)),
            ("auth login microsoft", () => new AuthCommands().Login(new() { Provider = AuthProvider.microsoft }, new(), both, default)),
            ("auth status microsoft", () => new AuthCommands().Status(new() { Provider = AuthProvider.microsoft }, new(), both, default)),
            ("auth logout microsoft", () => new AuthCommands().Logout(new() { Provider = AuthProvider.microsoft }, new(), both, default)),
            ("calendar sync", () => new CalendarCommands().Sync(new(), new(), both, new(), default)),
            ("drive publish", () => new DriveCommands().Publish(new(), new(), new(), both, new(), default)),
            ("registry sync", () => new RegistryCommands().Sync(new(), new(), both, new(), default)),
            ("registry import-grades", () => new RegistryCommands().ImportGrades(new() { QuizId = "1" }, new(), new(), both, new(), default)),
            ("registry history", () => new RegistryCommands().History(new(), new(), both, default)),
            ("registry history-topics", () => new RegistryCommands().HistoryTopics(new(), new(), both, default)),
        };
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            foreach (var (name, run) in cases)
            {
                stdout.GetStringBuilder().Clear();
                stderr.GetStringBuilder().Clear();
                Assert.Equal(2, await run());
                Assert.True(string.IsNullOrWhiteSpace(stdout.ToString()), $"stdout must stay empty for {name}.");
                Assert.Contains("Only one of --json and --jsonl", stderr.ToString());
            }
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    private static string NewTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NewHistoryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".registry-history");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = NewTempRoot();
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FixtureQuery : QueryCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
    }

    private sealed class FixtureCalendar(CalendarFake provider) : CalendarCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
        protected override Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
            => Task.FromResult<ICalendarSyncProvider>(provider);
    }

    private sealed class FixtureDrive(DriveFake provider) : DriveCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
        protected override Task<IDriveSyncProvider> Connect(IServiceProvider services, BuiltGoogleDriveConfig config, CancellationToken token)
            => Task.FromResult<IDriveSyncProvider>(provider);
    }

    private sealed class HistoryCommands(HistoryProvider provider) : RegistryCommands
    {
        protected override IRegistryHistoryProvider CreateHistoryProvider() => provider;
    }

    private sealed class HistoryProvider : IRegistryHistoryProvider
    {
        public Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(SettingsArguments settings, string? year, CancellationToken token)
            => Task.FromResult<IReadOnlyList<RegistryHistoryYearSummary>>([new("2024/25", [new("private-course", 1, ["private-group"])])]);
    }

    private sealed class HistoryTopicsCommands(HistoryTopicsProvider provider) : RegistryCommands
    {
        protected override IRegistryHistoryTopicsProvider CreateHistoryTopicsProvider() => provider;
    }

    private sealed class HistoryTopicsProvider : IRegistryHistoryTopicsProvider
    {
        public Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token)
        {
            var course = new RegistryHistoryCourse("canary-course", 1, new("https://private.test/groups/1"));
            var group = new RegistryHistoryGroupTopics("canary-group", new("https://private.test/LessonsList/1"),
                [new(new DateTime(2024, 10, 1), LessonType.Curs, "canary-historical-topic")]);
            var historical = new RegistryHistoryCourseTopics("2024/25", course, [group]);
            var currentGroup = group with { Lessons = [new(new DateTime(2025, 10, 1), LessonType.Curs, "canary-current-topic")] };
            var current = new RegistryHistoryCourseTopics("2025/26", course, [currentGroup]);
            return Task.FromResult(new RegistryHistoryTopicsAnalysis(["2025/26", "2024/25"], [current], new("2025/26", [new(course, historical)], [])));
        }
    }

    private sealed class CalendarFake : ICalendarSyncProvider
    {
        public List<string> Writes { get; } = [];
        private int _events;
        public Task<string> GetAccount(CancellationToken token) => Task.FromResult("actual@example.test");
        public Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token)
            => Task.FromResult<IReadOnlyList<CalendarDestination>>([new("old", "Lessons", false)]);
        public Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string id, CancellationToken token)
            => Task.FromResult<IReadOnlyList<CalendarEventIdentity>>([new("old-event", "Old")]);
        public Task DeleteCalendar(string id, CancellationToken token) { Writes.Add("delete " + id); return Task.CompletedTask; }
        public Task<string> CreateCalendar(string summary, CancellationToken token) { Writes.Add("calendar"); return Task.FromResult("new"); }
        public Task<string> CreateEvent(string id, Event desired, CancellationToken token)
        {
            Writes.Add("event " + id);
            return Task.FromResult("event-" + (++_events).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        public void Dispose() { }
    }

    private sealed class DriveFake : IDriveSyncProvider
    {
        public List<string> Writes { get; } = [];
        public Task<DriveAccount> GetAccount(CancellationToken token) => Task.FromResult(new DriveAccount("actual-account", "actual@example.test"));
        public Task<DriveDestination> FindFolder(string name, CancellationToken token)
            => Task.FromResult(new DriveDestination("actual-folder", "Actual folder"));
        public Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string id, CancellationToken token)
            => Task.FromResult<IReadOnlyList<DriveRemoteFile>>([new("old", "obsolete.txt")]);
        public Task Delete(string id, CancellationToken token) { Writes.Add("delete " + id); return Task.CompletedTask; }
        public Task<string> Create(string folder, string name, Stream input, CancellationToken token)
        {
            Writes.Add("create " + name);
            return Task.FromResult("created");
        }
        public Task<string> Update(string id, Stream input, CancellationToken token) { Writes.Add("update " + id); return Task.FromResult(id); }
        public void Dispose() { }
    }

    private sealed class CancellingWriter(CancellationTokenSource source, int cancelAfter) : StringWriter
    {
        private int _lines;
        public string Snapshot()
        {
            lock (this) return GetStringBuilder().ToString();
        }
        public override void WriteLine(string? value)
        {
            lock (this) base.WriteLine(value);
            if (Interlocked.Increment(ref _lines) == cancelAfter) source.Cancel();
        }
    }

    private sealed class FlakyWriter(params int[] failOn) : StringWriter
    {
        private readonly HashSet<int> _failOn = failOn.ToHashSet();
        private int _lines;
        public string Snapshot() => GetStringBuilder().ToString();
        public override void WriteLine(string? value)
        {
            if (_failOn.Contains(Interlocked.Increment(ref _lines)))
                throw new InvalidOperationException("synthetic console failure");
            base.WriteLine(value);
        }
    }

    private sealed class XdgIsolation : IDisposable
    {
        private readonly string? _previousConfig;
        private readonly string? _previousData;
        private readonly string _previousDirectory;
        public XdgIsolation()
        {
            _previousConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            _previousData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            _previousDirectory = Environment.CurrentDirectory;
            var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "config"));
            Directory.CreateDirectory(Path.Combine(root, "data"));
            Directory.CreateDirectory(Path.Combine(root, "work"));
            Root = root;
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(root, "config"));
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "data"));
            Environment.CurrentDirectory = Path.Combine(root, "work");
        }
        public string Root { get; }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousConfig);
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _previousData);
            Environment.CurrentDirectory = _previousDirectory;
            Directory.Delete(Root, recursive: true);
        }
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> CaptureJsonl(Func<Task<int>> run)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await run();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    private static async Task<string> WaitFor(Func<string> snapshot, Func<string, bool> ready, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            var current = snapshot();
            if (ready(current)) return current;
            await Task.Delay(20);
        }
        return snapshot();
    }

    private sealed class TestCommands(IRegistryProvider provider) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => provider;
        protected override IRegistryAccountLock CreateAccountLock() => new FakeLock();
    }

    private sealed class TestGradeCommands(IRegistryGradePlanner planner) : RegistryCommands
    {
        private sealed class Session : IRegistrySession
        {
            public RegistryDestination Target { get; } = new("synthetic-account", "https://registry.test/");
            public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) => Task.FromResult<IReadOnlyList<RegistrySyncAction>>([]);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private sealed class Provider : IRegistryProvider
        {
            public Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token)
                => Task.FromResult<IRegistrySession>(new Session());
        }
        protected override IRegistryProvider CreateProvider() => new Provider();
        protected override IRegistryGradePlanner CreateGradePlanner() => planner;
        protected override IRegistryAccountLock CreateAccountLock() => new FakeLock();
    }

    private sealed class FakeLock : IRegistryAccountLock, IAsyncDisposable
    {
        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token) => Task.FromResult<IAsyncDisposable>(this);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeProvider : IRegistryProvider, IRegistrySession
    {
        public Task? BlockSecond;
        public bool CancelSecond;
        public Exception? ActionFailure;
        public List<int> Executed { get; } = [];
        public RegistryDestination Target { get; } = new("synthetic-account", "https://registry.test/");
        public Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token) => Task.FromResult<IRegistrySession>(this);
        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token)
        {
            var kinds = new[] { "create", "update", "delete" };
            IReadOnlyList<RegistrySyncAction> actions = kinds.Select((kind, index) => new RegistrySyncAction(kind,
                Target.Destination, "synthetic-course", "synthetic-group", new(2026, 1, index + 1), "synthetic-topic", 2, true, null, async ct =>
                {
                    lock (Executed) Executed.Add(index);
                    if (ActionFailure is not null && index == 0) throw ActionFailure;
                    if (BlockSecond is not null && index == 1) await BlockSecond;
                    if (CancelSecond && index == 1) throw new OperationCanceledException();
                    await Task.CompletedTask;
                })).ToArray();
            return Task.FromResult(actions);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGradeProvider : IRegistryGradePlanner
    {
        public Task<RegistryGradePlan> Plan(IRegistrySession session, string quizId, CancellationToken token)
            => Task.FromResult(new RegistryGradePlan([], []));
    }

    private sealed class SyncCapture : System.IO.TextWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly object _gate = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value)
        {
            lock (_gate) _builder.AppendLine(value);
        }
        public override void Write(char value)
        {
            lock (_gate) _builder.Append(value);
        }
        public string Snapshot()
        {
            lock (_gate) return _builder.ToString();
        }
    }

    private sealed class LogCapture : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(this);
        public void Dispose() { }
        private sealed class Logger(LogCapture parent) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (parent.Messages) parent.Messages.Add($"{logLevel}:{formatter(state, exception)}");
            }
        }
    }

    private sealed class FixtureNavigator(CourseId course, GroupLink group) : IRegistrySyncNavigator
    {
        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) => Task.FromResult<IEnumerable<CourseLink>>([new(course, new("https://registry.test/course"))]);
        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link) => Task.FromResult<IEnumerable<GroupLink>>([group]);
        public async Task<IDocument> GetHtml(Uri uri) => await new HtmlParser().ParseDocumentAsync(uri.AbsolutePath == "/group"
            ? "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>"
            : "<html></html>");
        public Task SubmitLesson(IDocument document) => throw new InvalidOperationException("Preview submitted a lesson.");
        public Task SubmitDelete(IDocument document) => throw new InvalidOperationException("Preview submitted a deletion.");
    }
}

using System.Text.Json;
using Anton.LayeredData;
using CommandDotNet;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>How the CLI reports services that fail to end once the command has reported its result.</summary>
public sealed class CliHostTeardownTests
{
    private const string FailureMessage = "teardown failure";

    /// <summary>How a command reports a failure to clean up after its own failure.</summary>
    private const string CleanupFailurePrefix = "Cleaning up after the failure failed: ";

    /// <summary>How a planned scope reports resources that failed to close.</summary>
    private const string ScopeEndFailurePrefix = "Ending a scope failed";

    [Fact]
    public async Task JsonResultStandsAndTheExitReportsTheFailure()
    {
        var run = await RunWithFailingTeardown("--json");

        Assert.Equal(expected: CliExit.Unexpected, actual: run.Exit);
        using var result = JsonDocument.Parse(run.Output);
        Assert.Equal(
            expected: CliExit.Success,
            actual: result.RootElement.GetProperty("exitCode")
                .GetInt32());
        Assert.Contains(expectedSubstring: FailureMessage, actualString: run.Error);
    }

    [Fact]
    public async Task JsonlFinalStandsAndTheFailureIsAStderrRecord()
    {
        var run = await RunWithFailingTeardown("--jsonl");

        Assert.Equal(expected: CliExit.Unexpected, actual: run.Exit);
        var final = Assert.Single(Finals(run.Output));
        Assert.Equal(
            expected: CliExit.Success,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        var message = TeardownRecordMessage(final, run.Error);
        Assert.Equal(
            expected: $"Ending the CLI's services failed: {FailureMessage}",
            actual: message);
    }

    [Fact]
    public async Task FailedRootSetupStillEndsTheRootContainer()
    {
        // The root container should dispose it; the using only guards a failing test.
        using var probe = new DisposalProbe();

        void FailTreeSetup(IServiceCollection services)
        {
            CliTestHost.FixtureSchedule(services);
            services.AddSingleton(_ => probe);
            services.AddSingleton<TreeBuilder>(root =>
            {
                _ = root.GetRequiredService<DisposalProbe>();
                throw new InvalidOperationException("tree setup failure");
            });
        }

        var run = await RunCapturing(["query", "lessons", "--json", .. CliTestHost.UncachedSource()], FailTreeSetup);

        Assert.Equal(expected: CliExit.Unexpected, actual: run.Exit);
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task FailingWorkflowTeardownIsTheOnlyFinal()
    {
        string[] args = ["query", "free-hours", "--jsonl", .. CliTestHost.UncachedSource(), "--group", "IA2301"];
        var run = await RunCapturing(args, HoldFailingResource);

        Assert.Equal(expected: CliExit.Unexpected, actual: run.Exit);
        var final = Assert.Single(Finals(run.Output));
        Assert.Equal(
            expected: CliExit.Unexpected,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        var error = Assert.Single(final.GetProperty("errors")
            .EnumerateArray());
        Assert.StartsWith(expectedStartString: ScopeEndFailurePrefix, actualString: error.GetString());
    }

    /// <summary>A failed workflow whose scope then fails to end reports both, with the workflow's exit.</summary>
    [Theory]
    [InlineData("lessons", "--room", "NO-SUCH-ROOM")]
    [InlineData("free-hours", "--group", "NO-SUCH-GROUP")]
    public async Task FailingWorkflowTeardownKeepsTheCommandFailure(string command, string option, string unknown)
    {
        string[] args = ["query", command, "--jsonl", .. CliTestHost.UncachedSource(), option, unknown];
        var run = await RunCapturing(args, HoldFailingResource);

        Assert.Equal(expected: CliExit.InvalidArguments, actual: run.Exit);
        var final = Assert.Single(Finals(run.Output));
        Assert.Equal(
            expected: CliExit.InvalidArguments,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        var errors = final.GetProperty("errors")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();
        Assert.Equal(expected: 2, actual: errors.Length);
        Assert.Contains(expectedSubstring: unknown, actualString: errors[0]);
        Assert.StartsWith(
            expectedStartString: $"{CleanupFailurePrefix}{ScopeEndFailurePrefix}",
            actualString: errors[1]);
    }

    [Fact]
    public async Task FailingCleanupWhenOpeningKeepsTheSourceError()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var missing = Path.Combine(temp.Path, "missing");
        string[] args = ["query", "lessons", "--jsonl", .. CliTestHost.UncachedSource(missing)];
        var run = await RunCapturing(args, HoldFailingResource);

        AssertPairedFailure(run, CliExit.InvalidConfiguration, missing);
    }

    [Fact]
    public async Task FailingCleanupWhenOpeningKeepsTheCancellation()
    {
        using var cancellation = new CancellationTokenSource();

        void CancelWhileHoldingFailingResource(IServiceCollection services)
        {
            CliTestHost.FixtureSchedule(services);
            services.AddSingleton(cancellation);
            services.AddInitialized<ScheduleSource, CancellingSourceInitializer>();
        }

        string[] args = ["query", "lessons", "--jsonl", .. CliTestHost.UncachedSource()];
        var run = await RunCapturing(args, CancelWhileHoldingFailingResource, cancellationToken: cancellation.Token);

        AssertPairedFailure(run, CliExit.Cancelled, "Cancelled.");
    }

    [Fact]
    public async Task FailingInvocationTeardownKeepsTheCommandsExit()
    {
        static void AddScopedFailingTeardown(IServiceCollection services)
        {
            CliTestHost.FixtureSchedule(services);
            services.AddScoped<FailingTeardown>();
        }

        string[] args = ["query", "lessons", "--jsonl", "--before-slot", "0", .. CliTestHost.UncachedSource()];
        var run = await RunCapturing(args, AddScopedFailingTeardown, ResolveFailingTeardownBeforeInvocation);

        Assert.Equal(expected: CliExit.InvalidArguments, actual: run.Exit);
        var final = Assert.Single(Finals(run.Output));
        Assert.Equal(
            expected: CliExit.InvalidArguments,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        var message = TeardownRecordMessage(final, run.Error);
        Assert.StartsWith(
            expectedStartString: $"Ending the command's services failed: {ScopeEndFailurePrefix}",
            actualString: message);
    }

    /// <summary>
    /// A single final with the primary failure's exit and its error, followed by the failure to
    /// clean up after it.
    /// </summary>
    private static void AssertPairedFailure(CapturedRun run, int exit, string primaryError)
    {
        Assert.Equal(expected: exit, actual: run.Exit);
        var final = Assert.Single(Finals(run.Output));
        Assert.Equal(
            expected: exit,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        var errors = final.GetProperty("errors")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .ToArray();
        Assert.Equal(expected: 2, actual: errors.Length);
        Assert.Contains(expectedSubstring: primaryError, actualString: errors[0]);
        Assert.StartsWith(
            expectedStartString: $"{CleanupFailurePrefix}{ScopeEndFailurePrefix}",
            actualString: errors[1]);
    }

    /// <summary>The fixture schedule, loaded by a workflow that holds a resource failing to close.</summary>
    private static void HoldFailingResource(IServiceCollection services)
    {
        CliTestHost.FixtureSchedule(services);
        services.AddInitialized<ScheduleSource, ResourceHoldingSourceInitializer>();
    }

    private static Task<CapturedRun> RunWithFailingTeardown(string format)
    {
        static void AddFailingTeardown(IServiceCollection services)
        {
            CliTestHost.FixtureSchedule(services);
            services.AddSingleton<FailingTeardown>();
        }

        // The root container owns a singleton resolved through the invocation scope.
        string[] args = ["query", "lessons", format, .. CliTestHost.UncachedSource()];
        return RunCapturing(args, AddFailingTeardown, ResolveFailingTeardownBeforeInvocation);
    }

    private static void ResolveFailingTeardownBeforeInvocation(AppRunner<Commands> runner)
    {
        static Task<int> ResolveFailingTeardown(CommandContext context, ExecutionDelegate next)
        {
            _ = context.DependencyResolver!.Resolve(typeof(FailingTeardown));
            return next(context);
        }

        runner.Configure(builder => builder.UseMiddleware(ResolveFailingTeardown, MiddlewareSteps.InvokeCommand - 1));
    }

    private static async Task<CapturedRun> RunCapturing(
        string[] args,
        Action<IServiceCollection> services,
        Action<AppRunner<Commands>>? runner = null,
        CancellationToken cancellationToken = default)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await CliTestHost.Run(args, services, cancellationToken, runner);
            return new(Exit: exit, Output: stdout.ToString(), Error: stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static JsonElement[] Finals(string output)
    {
        return JsonLines(output)
            .Where(x => x.GetProperty("event").GetString() == "final")
            .ToArray();
    }

    /// <summary>
    /// The teardown failure: the last stderr record, of the result's command and run, continuing
    /// its stderr sequence, and the only one reporting a teardown.
    /// </summary>
    private static string TeardownRecordMessage(JsonElement final, string error)
    {
        static string Text(JsonElement record, string property)
        {
            var value = record.GetProperty(property);
            return value.GetString()!;
        }

        static bool ReportsTeardown(JsonElement record)
        {
            var message = Text(record, "message");
            return message.StartsWith("Ending ", StringComparison.Ordinal);
        }

        var records = JsonLines(error);
        Assert.Single(records, ReportsTeardown);
        var teardown = records[^1];
        Assert.True(ReportsTeardown(teardown));
        Assert.Equal(expected: "Error", actual: Text(teardown, "level"));
        var resultCommand = Text(final, "command");
        var teardownCommand = Text(teardown, "command");
        Assert.Equal(expected: resultCommand, actual: teardownCommand);
        var resultRunId = Text(final, "runId");
        var teardownRunId = Text(teardown, "runId");
        Assert.Equal(expected: resultRunId, actual: teardownRunId);
        Assert.Equal(
            expected: records.Length - 1,
            actual: teardown.GetProperty("seq")
                .GetInt32());
        return Text(teardown, "message");
    }

    private static JsonElement[] JsonLines(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
    }

    private readonly record struct CapturedRun(int Exit, string Output, string Error);

    private sealed class DisposalProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    /// <summary>Asynchronous only: the CLI ends its containers and scope resources asynchronously.</summary>
    private sealed class FailingTeardown : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => throw new InvalidOperationException(FailureMessage);
    }

    /// <summary>Resolves the schedule source as usual, holding a resource that fails to close.</summary>
    private sealed class ResourceHoldingSourceInitializer(ScopeResources resources, ScheduleSourceSelection selection)
        : IInitializer<ScheduleSource>
    {
        public ValueTask<ScheduleSource> InitializeAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA2000 // ScopeResources closes the resource when the scope ends.
            resources.Add(new FailingTeardown());
#pragma warning restore CA2000
            var source = selection.Resolve();
            return ValueTask.FromResult(source);
        }
    }

    /// <summary>Holds a resource that fails to close, then cancels the command and stops.</summary>
    private sealed class CancellingSourceInitializer(ScopeResources resources, CancellationTokenSource cancellation)
        : IInitializer<ScheduleSource>
    {
        public ValueTask<ScheduleSource> InitializeAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA2000 // ScopeResources closes the resource when the scope ends.
            resources.Add(new FailingTeardown());
#pragma warning restore CA2000
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The command's cancellation should reach the initializer.");
        }
    }
}

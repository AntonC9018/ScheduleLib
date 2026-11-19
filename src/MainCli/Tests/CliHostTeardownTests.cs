using System.Text.Json;
using CommandDotNet;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Cli;

/// <summary>How the CLI reports a root container that fails to end after the command has run.</summary>
public sealed class CliHostTeardownTests
{
    private const string FailureMessage = "teardown failure";

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
        var finals = JsonLines(run.Output)
            .Where(x => x.GetProperty("event").GetString() == "final")
            .ToArray();
        var final = Assert.Single(finals);
        Assert.Equal(
            expected: CliExit.Success,
            actual: final.GetProperty("exitCode")
                .GetInt32());
        var errors = JsonLines(run.Error)
            .Where(x => x.GetProperty("level").GetString() == "Error")
            .ToArray();
        var error = Assert.Single(errors);
        var message = error.GetProperty("message")
            .GetString();
        Assert.Contains(expectedSubstring: FailureMessage, actualString: message);
    }

    private static async Task<CapturedRun> RunWithFailingTeardown(string format)
    {
        static void AddFailingTeardown(IServiceCollection services)
        {
            CliTestHost.FixtureSchedule(services);
            services.AddSingleton<FailingTeardown>();
        }

        // The root container owns a singleton resolved through the invocation scope.
        static Task<int> ResolveFailingTeardown(CommandContext context, ExecutionDelegate next)
        {
            _ = context.DependencyResolver!.Resolve(typeof(FailingTeardown));
            return next(context);
        }

        static void ConfigureRunner(AppRunner<Commands> runner) =>
            runner.Configure(builder => builder.UseMiddleware(ResolveFailingTeardown, MiddlewareSteps.InvokeCommand - 1));

        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            string[] args = ["query", "lessons", format, .. CliTestHost.UncachedSource()];
            var exit = await CliTestHost.Run(args, AddFailingTeardown, runner: ConfigureRunner);
            return new(Exit: exit, Output: stdout.ToString(), Error: stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static JsonElement[] JsonLines(string text)
    {
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
    }

    private readonly record struct CapturedRun(int Exit, string Output, string Error);

    private sealed class FailingTeardown : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException(FailureMessage);
    }
}

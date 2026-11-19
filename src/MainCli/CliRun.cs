namespace ScheduleLib.Cli;

/// <summary>
/// One run of the CLI host and the output its result went to. Diagnostics written after the
/// result, such as teardown failures, go to the same output, so they share its
/// <c>command</c> and <c>runId</c> and continue its stderr <c>seq</c>.
/// </summary>
/// <remarks>
/// Commands create their <see cref="CliCommandContext"/> themselves, so a context reaches the run
/// of the host that invoked it through the async flow.
/// </remarks>
internal sealed class CliRun
{
    private static readonly AsyncLocal<CliRun?> Current = new();

    private readonly Lock _lock = new();
    private CliOutput? _resultOutput;

    private CliRun()
    {
    }

    /// <summary>Starts a run that the commands invoked from the calling async flow report to.</summary>
    public static CliRun Begin()
    {
        var run = new CliRun();
        Current.Value = run;
        return run;
    }

    /// <summary>Records <paramref name="output"/> as the result output of the current run, if any.</summary>
    public static void RecordStarted(CliOutput output) => Current.Value?.Record(output);

    /// <summary>
    /// Records <paramref name="output"/> as where the result goes. The first output stays: a
    /// command started by another command reports to its caller, which reports the result.
    /// </summary>
    public void Record(CliOutput output)
    {
        lock (_lock)
        {
            _resultOutput ??= output;
        }
    }

    /// <summary>
    /// The output the result went to. Without one, a host output in the requested format, which
    /// later diagnostics then share.
    /// </summary>
    public CliOutput ResultOutput(StartupRequest request)
    {
        lock (_lock)
        {
            if (_resultOutput is null)
            {
                var runId = Guid.NewGuid()
                    .ToString("N");
                _resultOutput = new CliOutput(
                    command: "cli",
                    runId: runId,
                    json: false,
                    jsonl: request.UsesJsonlOutput);
            }

            return _resultOutput;
        }
    }
}

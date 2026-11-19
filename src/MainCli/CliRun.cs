namespace ScheduleLib.Cli;

/// <summary>
/// One run of the CLI host and the output its result went to. Diagnostics written after the
/// result, such as teardown failures, go to the same output, so they share its
/// <c>command</c> and <c>runId</c> and continue its stderr <c>seq</c>.
/// </summary>
/// <remarks>
/// The host registers the run in its root container, and the commands resolved there create
/// their context through it. Commands that still construct their context directly report such
/// diagnostics on a host output instead.
/// </remarks>
public sealed class CliRun
{
    private readonly Lock _lock = new();
    private CliOutput? _resultOutput;

    internal CliRun()
    {
    }

    /// <summary>Creates the context of a command whose output receives this run's result.</summary>
    public CliCommandContext CreateContext(string command, ResultArguments arguments, CancellationToken token) =>
        new(command, arguments, token, this);

    /// <summary>
    /// Records <paramref name="output"/> as where the result goes. The first output stays: a
    /// command started by another command reports to its caller, which reports the result.
    /// </summary>
    internal void Record(CliOutput output)
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
    internal CliOutput ResultOutput(StartupRequest request)
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

namespace ScheduleLib.Cli;

/// <summary>
/// One run of the CLI host and the output its result went to. Diagnostics written after the
/// result, such as teardown failures and logs of services as they end, go to the same output,
/// so they share its <c>command</c> and <c>runId</c> and continue its stderr <c>seq</c>.
/// Logs keep going to that output until the run is disposed, after its services have ended.
/// </summary>
/// <remarks>
/// The host registers the run in its root container, and the commands resolved there create
/// their context through it. Commands that still construct their context directly report such
/// diagnostics on a host output instead.
/// </remarks>
public sealed class CliRun : IDisposable
{
    private readonly Lock _lock = new();
    private CliOutput? _resultOutput;
    private IDisposable? _logging;

    internal CliRun()
    {
    }

    /// <summary>Creates the context of a command whose output receives this run's result.</summary>
    public CliCommandContext CreateContext(string command, ResultArguments arguments, CancellationToken token) =>
        new(command, arguments, token, this);

    /// <summary>
    /// Takes <paramref name="output"/> as where the result goes and routes logs to it, unless an
    /// output was taken already: a command started by another command reports to its caller,
    /// which reports the result. Returns whether it was taken.
    /// </summary>
    internal bool Adopt(CliOutput output)
    {
        lock (_lock)
        {
            if (_resultOutput is not null)
            {
                return false;
            }

            Take(output);
            return true;
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
            if (_resultOutput is { } taken)
            {
                return taken;
            }

            var runId = Guid.NewGuid()
                .ToString("N");
            var output = new CliOutput(
                command: "cli",
                runId: runId,
                json: false,
                jsonl: request.UsesJsonlOutput);
            Take(output);
            return output;
        }
    }

    private void Take(CliOutput output)
    {
        _resultOutput = output;
        _logging = CliLogging.Use(output);
    }

    /// <summary>Routes logs back to where they went before the run took an output.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _logging?.Dispose();
            _logging = null;
        }
    }
}

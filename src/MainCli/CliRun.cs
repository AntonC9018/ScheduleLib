namespace ScheduleLib.Cli;

/// <summary>
/// Diagnostics written after the result, such as teardown failures and logs of services as they
/// end, go to the result's output, so they share its <c>command</c> and <c>runId</c> and continue
/// its stderr <c>seq</c>. Commands that still construct their context directly report such
/// diagnostics on a host output instead.
/// </summary>
public sealed class CliRun : IDisposable
{
    private readonly Lock _lock = new();
    private CliOutput? _resultOutput;
    private IDisposable? _logging;

    internal CliRun()
    {
    }

    public CliCommandContext CreateContext(string command, ResultArguments arguments, CancellationToken token) =>
        new(command, arguments, token, this);

    // A command started by another command reports to its caller, so only the first output is taken.
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

    public void Dispose()
    {
        lock (_lock)
        {
            _logging?.Dispose();
            _logging = null;
        }
    }
}

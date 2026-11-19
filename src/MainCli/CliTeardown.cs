namespace ScheduleLib.Cli;

/// <summary>
/// Ends services once the command has reported its outcome, before an enclosing <c>await using</c>
/// would, which then has nothing left to end. A teardown failure cannot change a result already
/// written, so no second result follows: the failure goes to stderr (a JSONL log record with
/// <c>--jsonl</c>), and a successful exit becomes <see cref="CliExit.Unexpected"/> while a failed
/// exit keeps the command's code.
/// </summary>
internal static class CliTeardown
{
    public static async Task<int> EndAfterResult(IAsyncDisposable services, string description, int exit, bool jsonl)
    {
        try
        {
            await services.DisposeAsync();
            return exit;
        }
        catch (Exception e)
        {
            ReportFailure($"Ending {description} failed: {e.Message}", jsonl);
            if (exit == CliExit.Success)
            {
                return CliExit.Unexpected;
            }

            return exit;
        }
    }

    private static void ReportFailure(string message, bool jsonl)
    {
        if (!jsonl)
        {
            Console.Error.WriteLine(message);
            return;
        }

        var runId = Guid.NewGuid()
            .ToString("N");
        new CliOutput(command: "cli", runId: runId, json: false, jsonl: true).WriteError(message);
    }
}

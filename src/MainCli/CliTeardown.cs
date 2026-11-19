namespace ScheduleLib.Cli;

/// <summary>
/// Ends services once the command has reported its outcome, before an enclosing <c>await using</c>
/// would, which then has nothing left to end. A teardown failure cannot change a result already
/// written, so no second result follows: the failure is an error on the result's output (with
/// <c>--jsonl</c>, a stderr log record of the same run), and a successful exit becomes
/// <see cref="CliExit.Unexpected"/> while a failed exit keeps the command's code.
/// </summary>
internal static class CliTeardown
{
    public static async Task<int> EndAfterResult(IAsyncDisposable services, string description, int exit, CliOutput output)
    {
        try
        {
            await services.DisposeAsync();
            return exit;
        }
        catch (Exception e)
        {
            output.WriteError($"Ending {description} failed: {e.Message}");
            if (exit == CliExit.Success)
            {
                return CliExit.Unexpected;
            }

            return exit;
        }
    }
}

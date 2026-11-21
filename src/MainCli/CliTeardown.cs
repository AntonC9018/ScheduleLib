namespace ScheduleLib.Cli;

/// <summary>
/// A teardown failure cannot change a result already written, so no second result follows: the
/// failure is an error on the result's output, and only a successful exit changes to unexpected.
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

using Microsoft.Extensions.Logging;

namespace ScheduleLib.Application.Config;

/// <summary>
/// Records why a provider failure was translated into a sanitized exception.
/// Provider messages may contain tokens, credentials or registry data (#228),
/// so only exception type names and a caller-vetted error code are logged;
/// the original exception stays available as <see cref="Exception.InnerException"/>.
/// </summary>
public static partial class TranslatedFailureLog
{
    /// <summary>Logs the cause of <paramref name="translated"/> at debug level and returns it for throwing.</summary>
    /// <param name="causeCode">A provider error code known not to contain private data, such as an OAuth error.</param>
    public static TException Translated<TException>(
        ILogger logger,
        TException translated,
        string? causeCode = null)
        where TException : Exception
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogTranslated(
                logger,
                translated.GetType().Name,
                DescribeCauses(translated),
                causeCode ?? "none");
        }
        return translated;
    }

    /// <summary>Logs a failure whose cause is classified but deliberately not echoed to the user.</summary>
    public static void Unreported(ILogger logger, string operation, Exception cause)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogUnreported(
                logger,
                operation,
                cause.GetType().Name,
                DescribeCauses(cause));
        }
    }

    /// <summary>Lists the inner exception types, outermost first.</summary>
    public static string DescribeCauses(Exception exception)
    {
        var names = new List<string>();
        for (var cause = exception.InnerException; cause is not null; cause = cause.InnerException)
        {
            names.Add(cause.GetType().Name);
        }
        if (names.Count == 0)
        {
            return "none";
        }
        return string.Join(" <- ", names);
    }

    [LoggerMessage(
        LogLevel.Debug,
        "{Failure} translated from {Causes} (code {CauseCode})")]
    private static partial void LogTranslated(
        ILogger logger,
        string failure,
        string causes,
        string causeCode);

    [LoggerMessage(
        LogLevel.Debug,
        "{Operation} failed with {Failure} (causes {Causes}); details withheld")]
    private static partial void LogUnreported(
        ILogger logger,
        string operation,
        string failure,
        string causes);
}

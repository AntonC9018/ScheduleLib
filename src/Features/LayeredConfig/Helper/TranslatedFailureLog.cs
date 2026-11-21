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
    // causeCode must be a provider error code known not to contain private data, such as an OAuth error.
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
                failure: translated.GetType().Name,
                causes: DescribeCauses(translated),
                causeCode: causeCode ?? "none");
        }
        return translated;
    }

    public static void Unreported(
        ILogger logger,
        string operation,
        Exception cause)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogUnreported(
                logger,
                operation: operation,
                failure: cause.GetType().Name,
                causes: DescribeCauses(cause));
        }
    }

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

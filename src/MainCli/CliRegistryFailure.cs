using System.Text.Json;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

internal static class CliRegistryFailure
{
    private enum ExitPolicy
    {
        Fixed,
        IoFailure,
        Unexpected,
        Streaming,
    }

    private readonly record struct FailureRule(
        ExitPolicy Policy,
        int FixedExitCode,
        Func<Exception, string> FormatMessage)
    {
        public static FailureRule Fixed(int exitCode, string message)
        {
            return new(
                ExitPolicy.Fixed,
                exitCode,
                _ => message);
        }

        public static FailureRule FixedFromMessage(int exitCode)
        {
            return new(
                ExitPolicy.Fixed,
                exitCode,
                error => error.Message);
        }

        public static FailureRule IoWithCause(string summary)
        {
            return new(
                ExitPolicy.IoFailure,
                0,
                error => WithCause(summary, error));
        }

        public static FailureRule UnexpectedWithCause(string summary)
        {
            return new(
                ExitPolicy.Unexpected,
                0,
                error => WithCause(summary, error));
        }

        public static FailureRule StreamingWithMessage(string message)
        {
            return new(
                ExitPolicy.Streaming,
                0,
                _ => message);
        }

        public int ResolveExitCode(bool attempted, bool grades)
        {
            if (Policy == ExitPolicy.Fixed)
            {
                return FixedExitCode;
            }

            if (attempted)
            {
                return CliExit.Partial;
            }

            if (Policy == ExitPolicy.IoFailure)
            {
                return CliExit.IoFailure;
            }

            if (Policy == ExitPolicy.Unexpected)
            {
                if (grades)
                {
                    return CliExit.Unexpected;
                }

                return CliExit.IoFailure;
            }

            return CliExit.Unexpected;
        }

        public CliFailure Create(
            Exception error,
            bool attempted,
            bool grades)
        {
            var exitCode = ResolveExitCode(attempted: attempted, grades: grades);
            var message = FormatMessage(error);
            return new(exitCode, [message]);
        }
    }

    private static readonly IReadOnlyDictionary<Type, FailureRule> SharedRules = new Dictionary<Type, FailureRule>
    {
        [typeof(CliOutputStreamingException)] = FailureRule.StreamingWithMessage(
            "Output streaming failed. Completed actions remain applied; inspect the registry before retrying."),
        [typeof(OperationCanceledException)] = FailureRule.Fixed(CliExit.Cancelled, "Cancelled."),
        [typeof(RegistryAuthenticationException)] = FailureRule.FixedFromMessage(CliExit.AuthenticationRequired),
        [typeof(LocalOperationBusyException)] = FailureRule.FixedFromMessage(CliExit.Busy),
        [typeof(JsonException)] = FailureRule.FixedFromMessage(CliExit.InvalidConfiguration),
        [typeof(DirectoryNotFoundException)] = FailureRule.FixedFromMessage(CliExit.InvalidConfiguration),
        [typeof(FileNotFoundException)] = FailureRule.FixedFromMessage(CliExit.InvalidConfiguration),
        [typeof(InvalidScheduleSourceException)] = FailureRule.FixedFromMessage(CliExit.InvalidConfiguration),
        [typeof(ScheduleBuildException)] = FailureRule.FixedFromMessage(CliExit.InvalidConfiguration),
        [typeof(ArgumentException)] = FailureRule.FixedFromMessage(CliExit.InvalidArguments),
        [typeof(PlatformNotSupportedException)] = FailureRule.FixedFromMessage(CliExit.Unsupported),
    };

    private static readonly IReadOnlyDictionary<Type, FailureRule> GradeRules = new Dictionary<Type, FailureRule>
    {
        [typeof(IOException)] = FailureRule.IoWithCause(
            "Registry input/output operation failed. No automatic retry was performed."),
        [typeof(HttpRequestException)] = FailureRule.IoWithCause(
            "Registry/Moodle remote read failed. No automatic retry was performed."),
        [typeof(InvalidOperationException)] = FailureRule.IoWithCause(
            "Registry/Moodle required form or report markup was unavailable. No automatic retry was performed."),
    };

    private static readonly FailureRule GradeFallback = FailureRule.UnexpectedWithCause(
        "Unexpected registry operation failure. No automatic retry was performed.");

    private static readonly FailureRule SyncFallback = FailureRule.UnexpectedWithCause(
        "Registry operation failed. No automatic retry was performed.");

    public static CliFailure Classify(
        Exception error,
        bool attempted,
        bool grades)
    {
        var rule = LookupRule(error.GetType(), grades);
        return rule.Create(
            error,
            attempted: attempted,
            grades: grades);
    }

    private static FailureRule LookupRule(Type exceptionType, bool grades)
    {
        for (var current = exceptionType; current is not null; current = current.BaseType)
        {
            if (SharedRules.TryGetValue(current, out var sharedRule))
            {
                return sharedRule;
            }

            if (grades && GradeRules.TryGetValue(current, out var gradeRule))
            {
                return gradeRule;
            }
        }

        if (grades)
        {
            return GradeFallback;
        }

        return SyncFallback;
    }

    /// <summary>
    /// Appends the underlying exception so unclassified failures remain diagnosable.
    /// Inner exceptions are included because wrappers often carry no useful message.
    /// </summary>
    private static string WithCause(string summary, Exception error)
    {
        var causes = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
        {
            var exceptionType = current.GetType().FullName;
            causes.Add($"{exceptionType}: {current.Message}");
        }
        var cause = string.Join(" ---> ", causes);
        return $"{summary} Cause: {cause}";
    }
}

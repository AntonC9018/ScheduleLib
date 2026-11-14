using System.Collections.Immutable;

namespace ScheduleLib.OnlineRegistry;

public sealed class RegistryOperationRejectedException(string operation)
    : RegistrySubmissionRejectedException($"Registry rejected {operation}.")
{
    public string Operation { get; } = operation;
}

public sealed class RegistryOperationResponseUnconfirmedException(string operation)
    : IOException($"Registry {operation} response could not be confirmed.")
{
    public string Operation { get; } = operation;
}

public sealed class RegistryLoginRedirectRejectedException()
    : RegistrySubmissionRejectedException("Registry submission redirected to login.");

public sealed class RegistryFormValidationRejectedException : RegistrySubmissionRejectedException
{
    public ImmutableArray<string> ValidationErrors { get; }

    public RegistryFormValidationRejectedException(IEnumerable<string> validationErrors)
        : this(CreateSnapshot(validationErrors))
    {
    }

    private RegistryFormValidationRejectedException(ImmutableArray<string> validationErrors)
        : base(FormatMessage(validationErrors))
    {
        ValidationErrors = validationErrors;
    }

    private static ImmutableArray<string> CreateSnapshot(IEnumerable<string> validationErrors)
    {
        ArgumentNullException.ThrowIfNull(validationErrors);
        return [.. validationErrors];
    }

    private static string FormatMessage(ImmutableArray<string> validationErrors)
    {
        if (validationErrors.IsDefaultOrEmpty)
        {
            return "Registry form validation failed.";
        }

        var joined = string.Join("; ", validationErrors);
        return $"Registry form validation failed: {joined}";
    }
}

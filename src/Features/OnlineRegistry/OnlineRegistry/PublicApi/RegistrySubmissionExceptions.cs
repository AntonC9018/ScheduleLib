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

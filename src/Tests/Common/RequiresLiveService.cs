namespace Tests.Common;

/// <summary>External services that opt-in live tests may contact.</summary>
public static class ExternalServices
{
    public const string UsmStudentRegistry = "crd.usm.md/studregistry";
}

/// <summary>
/// A fact that contacts a live external service.
/// It is skipped unless <see cref="OptInVariable"/> is set to 1;
/// once opted in, missing credentials or configuration are failures, not skips.
/// </summary>
public sealed class RequiresLiveServiceAttribute : FactAttribute
{
    public const string OptInVariable = "SCHEDULELIB_LIVE_TESTS";

    public RequiresLiveServiceAttribute(string service)
    {
        Service = service;
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1")
        {
            Skip = $"Requires live service {service}; set {OptInVariable}=1 to run.";
        }
    }

    public string Service { get; }
}

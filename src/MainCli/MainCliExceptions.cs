namespace ScheduleLib.Cli;

public sealed class EmptyMicrosoftAuthSectionException()
    : InvalidOperationException("Microsoft section is empty.");

public sealed class MissingGoogleLoginCredentialsException()
    : InvalidOperationException("Configure Google login credentials before execution.");

public sealed class IntakeLabCleanupNotSupportedException()
    : NotSupportedException("Intake lab cleanup is not supported by this registry session.");

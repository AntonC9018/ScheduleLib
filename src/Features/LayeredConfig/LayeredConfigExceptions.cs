namespace ScheduleLib.Application.Config;

public enum EnumFilterConflictKind
{
    IncludeWhileIgnoring,
    IgnoreWhileIncluding,
}

public sealed class ConflictingEnumConverterFilterException(
    EnumFilterConflictKind kind,
    Type enumType)
    : InvalidOperationException(CreateMessage(kind, enumType))
{
    public EnumFilterConflictKind Kind { get; } = kind;
    public Type EnumType { get; } = enumType;

    private static string CreateMessage(EnumFilterConflictKind kind, Type enumType)
    {
        return kind switch
        {
            EnumFilterConflictKind.IncludeWhileIgnoring =>
                $"Cannot include values for `{enumType.Name}` when ignoring.",
            EnumFilterConflictKind.IgnoreWhileIncluding =>
                $"Cannot ignore values for `{enumType.Name}` when including.",
            _ => $"Conflicting enum filter operation ({kind}) for `{enumType.Name}`.",
        };
    }
}

public sealed class TeacherLayerConfigMissingException()
    : InvalidOperationException("Teacher layer config must be explicitly set!");

public sealed class GoogleDriveCredentialsMissingException()
    : InvalidOperationException("No credentials config");

public sealed class GoogleDriveFolderMissingException()
    : InvalidOperationException("No drive folder configured");

public sealed class GoogleCalendarCredentialsMissingException()
    : InvalidOperationException("Credentials not provided for Calendar");

public sealed class PrimaryGoogleCalendarNotAllowedException()
    : InvalidOperationException("Using `primary` calendar might clear all your events! Don't");

public sealed class GoogleCredentialsPathMissingException()
    : InvalidOperationException("No credentials path with SaveCredentials configured");

public sealed class GoogleApiKeysSourceMissingException()
    : InvalidOperationException("No API keys source configured");

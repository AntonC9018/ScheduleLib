namespace ScheduleLib.Application.Core;

public sealed class InvalidScheduleFolderNameException(string folderName)
    : InvalidOperationException($"The folders must be named in the format 'DD.MM.YYYY'. Found this: {folderName}")
{
    public string FolderName { get; } = folderName;
}

public sealed class NoScheduleCellsFoundException(string directoryName)
    : FormatException($"No schedule cells in PDF sources under {directoryName}.")
{
    public string DirectoryName { get; } = directoryName;
}

internal sealed record InvalidPreDefenseCommissionMember(
    int CommissionNumber,
    int MemberNumber,
    string Name);

internal static class PreDefenseInvalidDataExceptionFactory
{
    internal static InvalidDataException ForInvalidMemberName(
        int commissionNumber,
        int memberNumber,
        string name,
        Exception innerException)
    {
        // Preserve the sealed framework type and its existing catch contract.
#pragma warning disable CR1600 // Typed context is attached below because this exception cannot be subclassed.
        var exception = new InvalidDataException(
            $"Pre-defense commission {commissionNumber} member {memberNumber} has an invalid name '{name}': {innerException.Message}",
            innerException);
#pragma warning restore CR1600
        exception.Data[typeof(InvalidPreDefenseCommissionMember)] = new InvalidPreDefenseCommissionMember(
            CommissionNumber: commissionNumber,
            MemberNumber: memberNumber,
            name);
        return exception;
    }
}

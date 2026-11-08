namespace ScheduleLib.Theses.Parsing;

public sealed class UnexpectedDriveFileFormatException(
    string uploadedFileTypeString,
    DriveFileType expectedFileType,
    string? actualMimeType)
    : InvalidOperationException(
        $"The file is not in the expected format '{uploadedFileTypeString}' ({expectedFileType}). The actual mime type is '{actualMimeType}'")
{
    public string UploadedFileTypeString { get; } = uploadedFileTypeString;
    public DriveFileType ExpectedFileType { get; } = expectedFileType;
    public string? ActualMimeType { get; } = actualMimeType;
}

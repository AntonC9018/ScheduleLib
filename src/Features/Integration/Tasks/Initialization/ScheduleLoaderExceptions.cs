namespace ScheduleLib.Application.Core;

public sealed class ScheduleFileReadException(string filePath, Exception innerException)
    : InvalidOperationException($"Error while reading {filePath}", innerException)
{
    public string FilePath { get; } = filePath;
}

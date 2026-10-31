namespace FmiScheduleImport;

public sealed class FutureScheduleUploadDateException(DateOnly period)
    : FormatException($"Schedule upload date {period:yyyy-MM-dd} is in the future.")
{
    public DateOnly Period { get; } = period;
}

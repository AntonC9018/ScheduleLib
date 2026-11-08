namespace ScheduleLib.Import.Pdf;

/// <summary>Coordinate comparisons with an explicit tolerance measured in PDF points.</summary>
internal readonly record struct PdfCoordinateTolerance(double Distance)
{
    // Alignment is strict, unlike the inclusive coverage comparisons below.
    public bool AreClose(double first, double second)
    {
        return Math.Abs(first - second) < Distance;
    }

    public bool AreSeparated(double first, double second)
    {
        return Math.Abs(first - second) > Distance;
    }

    public bool ExceedsGap(double position, double previousPosition)
    {
        return position - previousPosition > Distance;
    }

    public bool IsAtMost(double value, double upperBound)
    {
        return value <= upperBound + Distance;
    }

    public bool IsAtLeast(double value, double lowerBound)
    {
        return value >= lowerBound - Distance;
    }
}

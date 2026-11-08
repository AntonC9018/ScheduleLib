namespace ScheduleLib.Mathematics;

public static class DoubleExtensions
{
    public static double Midpoint(this double start, double end)
    {
        return (start + end) * 0.5;
    }
}

namespace ScheduleLib;

public enum Semester
{
    Sem1,
    Sem2,
    Count = 2,
    Invalid = -1,
}

public static class SemesterHelper
{
    public static int AsOrdinal(this Semester semester)
    {
        return (int) semester + 1;
    }

    public static Semester FromInt(int value)
    {
        return value switch
        {
            1 => Semester.Sem1,
            2 => Semester.Sem2,
            _ => throw new ArgumentException("Invalid semester value", nameof(value)),
        };
    }
}

namespace ScheduleLib.OnlineRegistry;

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
}

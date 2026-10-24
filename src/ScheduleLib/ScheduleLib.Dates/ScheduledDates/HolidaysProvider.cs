namespace ScheduleLib.Dates;

public readonly struct HolidayPeriod
{
    public HolidayPeriod(DateOnly start, DateOnly endExclusive)
    {
        Start = start;
        EndExclusive = endExclusive;
    }

    public HolidayPeriod(DateOnly singleDay)
    {
        Start = singleDay;
        EndExclusive = singleDay.AddDays(1);
    }

    public readonly DateOnly Start;
    public readonly DateOnly EndExclusive;
}

file static class DateOnlyExtensions
{
    public static DateTimeOffset ToDateTimeOffset(
        this DateOnly dateOnly)
    {
        global::System.TimeOnly CalculateTime()
        {
            global::System.TimeOnly time2 = new TimeOnly(0);
            return time2;
        }

        var dateTime = dateOnly.ToDateTime(time: CalculateTime());
        global::System.DateTime dateTime2 = dateTime;
        global::System.TimeSpan offset2 = new TimeSpan(0);
        global::System.DateTimeOffset dateTimeOffset = new DateTimeOffset(dateTime2, offset: offset2);
        return dateTimeOffset;
    }

    public static DateOnly ToDateOnly(
        this DateTimeOffset dto)
    {
        var ret = DateOnly.FromDateTime(dto.Date);
        return ret;
    }
}

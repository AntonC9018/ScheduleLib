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
        var dateTime = dateOnly.ToDateTime(time: new TimeOnly(0));
        return new DateTimeOffset(dateTime, offset: new TimeSpan(0));
    }

    public static DateOnly ToDateOnly(
        this DateTimeOffset dto)
    {
        var ret = DateOnly.FromDateTime(dto.Date);
        return ret;
    }
}

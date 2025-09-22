namespace MainCli.Helper;

public static class DateHelpers
{
    public static DateOnly SetMonth(this DateOnly date, int month)
    {
        return date.AddMonths(month - date.Month);
    }
    public static DateOnly SetDay(this DateOnly date, int day)
    {
        return date.AddDays(day - date.Day);
    }
}

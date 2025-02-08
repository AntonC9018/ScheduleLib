using System.Diagnostics;

namespace ScheduleLib.OnlineRegistry;

public interface IAllScheduledDateProvider
{
    public readonly struct Params
    {
        public required Parity Parity { get; init; }
        public required DayOfWeek Day { get; init; }
    }
    IEnumerable<DateOnly> Dates(Params p);
}

public sealed class ManualAllScheduledDateProvider
    : IAllScheduledDateProvider
{
    public required StudyWeek[] StudyWeeks { private get; init; }

    public IEnumerable<DateOnly> Dates(IAllScheduledDateProvider.Params p)
    {
        foreach (var week in StudyWeeks)
        {
            if (!IsParityMatch())
            {
                continue;
            }
            const int weekdayCount = 7;
            var offset = (p.Day - DayOfWeek.Monday + weekdayCount) % weekdayCount;
            var ret = week.MondayDate.AddDays(offset);
            yield return ret;

            continue;

            bool IsParityMatch()
            {
                switch (p.Parity)
                {
                    case Parity.EveryWeek:
                    {
                        return true;
                    }
                    case Parity.OddWeek:
                    {
                        return week.IsOddWeek;
                    }
                    case Parity.EvenWeek:
                    {
                        return !week.IsOddWeek;
                    }
                    default:
                    {
                        Debug.Fail("Impossible value of parity");
                        return false;
                    }
                }
            }
        }
    }
}

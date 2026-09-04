namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public ListBuilder<PeriodBuilderModel> Periods = new();
}

public sealed class PeriodBuilderModel
{
    public required DateOnly Start;
    public DateOnly EndExclusive = default;
}

public readonly struct PeriodBuilder
{
    public required ScheduleBuilder Schedule { get; init; }
    public required PeriodId Id { get; init; }
    public PeriodBuilderModel Model => Schedule.Periods.Ref(Id.Value);
    public static implicit operator PeriodId(PeriodBuilder r) => r.Id;
}

public static class PeriodBuilderHelper
{
    public static PeriodBuilder Period(this ScheduleBuilder s, DateOnly start)
    {
        var ret = s.Periods.New();
        ret.Value = new()
        {
            Start = start,
        };
        return new()
        {
            Id = new(ret.Id),
            Schedule = s,
        };
    }

    internal static void ValidatePeriods(ScheduleBuilder s)
    {
        foreach (var period in s.Periods.List)
        {
            if (period.EndExclusive == default)
            {
                continue;
            }
            if (period.EndExclusive < period.Start)
            {
                throw new InvalidPeriodException("End date is before start date.");
            }
        }
    }
    //
    // public static void End(this PeriodBuilder b, DateOnly end)
    // {
    //     b.Model.EndExclusive = end;
    // }
}

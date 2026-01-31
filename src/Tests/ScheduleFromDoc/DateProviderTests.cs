using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;

namespace ScheduleFromDoc.Tests;

public sealed class DateProviderTests
{
    [Fact]
    public void DateToEventTransformTest()
    {
        DateOnly Date(int day)
        {
            return new(year: 2025, month: 9, day: day);
        }

        var result = ScheduledDatesToEventsTransformerHelper.TransformDatesToEvents(
            [
                Date(day: 1),
                Date(day: 4),
                Date(day: 7),
                Date(day: 10),
                Date(day: 11),
                Date(day: 12),
                Date(day: 15),
                Date(day: 18),
            ], interval: 3)
            .ToArray();
        Assert.Collection(result,
            e1 =>
            {
                var ev = Event.CreateRecurring(Date(1), count: 4, interval: 3);
                Assert.Equal(ev, e1);
            },
            e2 =>
            {
                var ev = Event.CreateSingle(Date(11));
                Assert.Equal(ev, e2);
            },
            e2 =>
            {
                var ev = Event.CreateRecurring(Date(12), count: 3, interval: 3);
                Assert.Equal(ev, e2);
            });
    }
}

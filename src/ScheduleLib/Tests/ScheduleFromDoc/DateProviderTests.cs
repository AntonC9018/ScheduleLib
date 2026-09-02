using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Dates;

namespace ScheduleFromDoc.Tests;

public sealed class DateProviderTests
{
    [Fact]
    public void AcademicCalendar2026_2027MatchesPublishedDates()
    {
        var semesterIntervals = ScheduleLib.ScheduleDefaults.Config.SemesterIntervalProvider();
        AssertRange(grade: 1, Semester.Sem1, new(2026, 9, 1), new(2026, 12, 13));
        AssertRange(grade: 2, Semester.Sem1, new(2026, 9, 1), new(2026, 12, 13));
        AssertRange(grade: 3, Semester.Sem1, new(2026, 9, 1), new(2026, 12, 13));
        AssertRange(grade: 1, Semester.Sem2, new(2027, 2, 1), new(2027, 5, 1));
        AssertRange(grade: 2, Semester.Sem2, new(2027, 2, 1), new(2027, 5, 1));
        AssertRange(grade: 3, Semester.Sem2, new(2027, 2, 22), new(2027, 4, 11));

        var weeks = ScheduleLib.ScheduleDefaults.Config.StudyWeeks;
        Assert.Equal(28, weeks.Length);
        Assert.Equal(new DateOnly(2026, 8, 31), weeks[0].MondayDate);
        Assert.False(weeks[0].IsOddWeek);
        Assert.Equal(new DateOnly(2026, 12, 7), weeks[14].MondayDate);
        Assert.Equal(new DateOnly(2027, 2, 1), weeks[15].MondayDate);
        Assert.True(weeks[15].IsOddWeek);
        Assert.Equal(new DateOnly(2027, 4, 26), weeks[^1].MondayDate);

        for (int i = 1; i < weeks.Length; i++)
        {
            Assert.NotEqual(weeks[i - 1].IsOddWeek, weeks[i].IsOddWeek);
        }

        Assert.Collection(
            ScheduleLib.ScheduleDefaults.Config.HolidayPeriods,
            period => AssertPeriod(period, new(2026, 10, 14), new(2026, 10, 15)),
            period => AssertPeriod(period, new(2027, 1, 1), new(2027, 1, 25)),
            period => AssertPeriod(period, new(2027, 5, 1), new(2027, 5, 2)),
            period => AssertPeriod(period, new(2027, 5, 2), new(2027, 5, 11)));

        void AssertRange(int grade, Semester semester, DateOnly start, DateOnly endInclusive)
        {
            var builder = new ScheduleBuilder();
            var groupSlot = builder.Groups.New();
            groupSlot.Value = new Group
            {
                Name = $"TEST-{grade}",
                Grade = new(grade),
                GroupNumber = 1,
                QualificationType = QualificationType.Licenta,
                Faculty = new("Test"),
                AttendanceMode = AttendanceMode.Zi,
                Language = Language.Ro,
            };
            var schedule = builder.Build();
            var actual = semesterIntervals.GetSemesterInterval(new()
            {
                Schedule = schedule,
                GroupId = new(groupSlot.Id),
                Semester = semester,
            });

            Assert.Equal(start, actual.Start);
            Assert.Equal(endInclusive, actual.EndInclusive);
        }

        static void AssertPeriod(HolidayPeriod period, DateOnly start, DateOnly endExclusive)
        {
            Assert.Equal(start, period.Start);
            Assert.Equal(endExclusive, period.EndExclusive);
        }
    }

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

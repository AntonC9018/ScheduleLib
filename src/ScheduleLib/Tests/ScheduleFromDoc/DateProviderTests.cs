using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Dates;

namespace ScheduleFromDoc.Tests;

public sealed class DateProviderTests
{
    [Theory]
    [InlineData(AttendanceMode.DualZi, Semester.Sem1)]
    [InlineData(AttendanceMode.DualZi, Semester.Sem2)]
    [InlineData(AttendanceMode.DualFr, Semester.Sem1)]
    [InlineData(AttendanceMode.DualFr, Semester.Sem2)]
    public void DualDatesDoNotFallBackToOrdinaryAttendance(AttendanceMode mode, Semester semester)
    {
        var builder = new ScheduleBuilder();
        var groupSlot = builder.Groups.New();
        groupSlot.Value = new Group
        {
            Name = "DU-IA2601",
            Grade = new(1),
            GroupNumber = 1,
            QualificationType = QualificationType.Licenta,
            Faculty = new("IA"),
            AttendanceMode = mode,
            Language = Language.Ro,
        };
        var groupId = new GroupId(groupSlot.Id);
        var schedule = builder.Build();
        var ranges = new CurrentYearSemesterIntervalBuilder();
        foreach (var ordinaryMode in new[]
                 {
                     AttendanceMode.Zi,
                     AttendanceMode.FrecventaRedusa,
                 })
        {
            ranges.Scope(scope =>
            {
                scope.AttendanceMode(ordinaryMode);
                scope.QualificationType(QualificationType.Licenta);
                scope.Year(2026);
                scope.Semester(semester);
                scope.LessonsStart(month: 9, day: 1);
                scope.LessonsEndInclusive(month: 12, day: 13);
                scope.Range(range =>
                {
                    var yearDateRangeBuilder = range;
                    Grade grade = new(1);
                    yearDateRangeBuilder.Grade(grade);
                });
            });
        }
        var intervals = ranges.Build();
        Assert.Throws<MissingSemesterDateRangeException>(() => intervals.GetSemesterInterval(new()
        {
            Schedule = schedule,
            GroupId = groupId,
            Semester = semester,
        }));
    }

    [Theory]
    [InlineData(1, Semester.Sem1)]
    [InlineData(2, Semester.Sem1)]
    [InlineData(3, Semester.Sem1)]
    [InlineData(1, Semester.Sem2)]
    [InlineData(2, Semester.Sem2)]
    [InlineData(3, Semester.Sem2)]
    public void DefaultDualZiDatesMatchZiForEachGradeAndSemester(int grade, Semester semester)
    {
        var builder = new ScheduleBuilder();
        var zi = builder.Groups.New();
        zi.Value = GroupFor(AttendanceMode.Zi);
        var dual = builder.Groups.New();
        dual.Value = GroupFor(AttendanceMode.DualZi);
        var schedule = builder.Build();
        var intervals = ScheduleLib.ScheduleDefaults.Config.SemesterIntervalProvider();
        var ordinaryRange = intervals.GetSemesterInterval(new()
        {
            Schedule = schedule,
            GroupId = new(zi.Id),
            Semester = semester,
        });
        var dualRange = intervals.GetSemesterInterval(new()
        {
            Schedule = schedule,
            GroupId = new(dual.Id),
            Semester = semester,
        });
        Assert.Equal(expected: ordinaryRange, actual: dualRange);

        Group GroupFor(AttendanceMode mode)
        {
            return new()
            {
                Name = mode == AttendanceMode.Zi ? $"IA-{grade}" : $"DU-IA-{grade}",
                Grade = new(grade),
                GroupNumber = 1,
                QualificationType = QualificationType.Licenta,
                Faculty = new("IA"),
                AttendanceMode = mode,
                Language = Language.Ro,
            };
        }
    }

    [Fact]
    public void AcademicCalendar2026_2027MatchesPublishedDates()
    {
        var semesterIntervals = ScheduleLib.ScheduleDefaults.Config.SemesterIntervalProvider();
        int grade2 = 1;
        var semester2 = Semester.Sem1;
        DateOnly start2 = new(year: 2026,
            month: 9,
            day: 1);
        DateOnly endInclusive2 = new(year: 2026,
            month: 12,
            day: 13);
        AssertRange(grade: grade2,
            semester2,
            start: start2,
            endInclusive: endInclusive2);
        int grade3 = 2;
        var semester3 = Semester.Sem1;
        DateOnly start3 = new(year: 2026,
            month: 9,
            day: 1);
        DateOnly endInclusive3 = new(year: 2026,
            month: 12,
            day: 13);
        AssertRange(grade: grade3,
            semester3,
            start: start3,
            endInclusive: endInclusive3);
        int grade4 = 3;
        var semester4 = Semester.Sem1;
        DateOnly start4 = new(year: 2026,
            month: 9,
            day: 1);
        DateOnly endInclusive4 = new(year: 2026,
            month: 12,
            day: 13);
        AssertRange(grade: grade4,
            semester4,
            start: start4,
            endInclusive: endInclusive4);
        int grade5 = 1;
        var semester5 = Semester.Sem2;
        DateOnly start5 = new(year: 2027,
            month: 2,
            day: 1);
        DateOnly endInclusive5 = new(year: 2027,
            month: 5,
            day: 1);
        AssertRange(grade: grade5,
            semester5,
            start: start5,
            endInclusive: endInclusive5);
        int grade6 = 2;
        var semester6 = Semester.Sem2;
        DateOnly start6 = new(year: 2027,
            month: 2,
            day: 1);
        DateOnly endInclusive6 = new(year: 2027,
            month: 5,
            day: 1);
        AssertRange(grade: grade6,
            semester6,
            start: start6,
            endInclusive: endInclusive6);
        int grade7 = 3;
        var semester7 = Semester.Sem2;
        DateOnly start7 = new(year: 2027,
            month: 2,
            day: 22);
        DateOnly endInclusive7 = new(year: 2027,
            month: 4,
            day: 11);
        AssertRange(grade: grade7,
            semester7,
            start: start7,
            endInclusive: endInclusive7);

        var weeks = ScheduleLib.ScheduleDefaults.Config.StudyWeeks;
        Assert.Equal(expected: 28, actual: weeks.Length);
        var expected2 = new DateOnly(year: 2026,
            month: 8,
            day: 31);
        var actual2 = weeks[0].MondayDate;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.False(weeks[0].IsOddWeek);
        var expected3 = new DateOnly(year: 2026,
            month: 12,
            day: 7);
        var actual3 = weeks[14].MondayDate;
        Assert.Equal(expected: expected3, actual: actual3);
        var expected4 = new DateOnly(year: 2027,
            month: 2,
            day: 1);
        var actual4 = weeks[15].MondayDate;
        Assert.Equal(expected: expected4, actual: actual4);
        Assert.True(weeks[15].IsOddWeek);
        var expected5 = new DateOnly(year: 2027,
            month: 4,
            day: 26);
        var actual5 = weeks[^1].MondayDate;
        Assert.Equal(expected: expected5, actual: actual5);

        for (int i = 1; i < weeks.Length; i++)
        {
            Assert.NotEqual(expected: weeks[i - 1].IsOddWeek, actual: weeks[i].IsOddWeek);
        }

        Assert.Collection(
            ScheduleLib.ScheduleDefaults.Config.HolidayPeriods,
            period =>
            {
                var period2 = period;
                DateOnly start2 = new(year: 2026,
                    month: 10,
                    day: 14);
                DateOnly endExclusive2 = new(year: 2026,
                    month: 10,
                    day: 15);
                AssertPeriod(period2,
                    start: start2,
                    endExclusive: endExclusive2);
            },
            period =>
            {
                var period2 = period;
                DateOnly start2 = new(year: 2027,
                    month: 1,
                    day: 1);
                DateOnly endExclusive2 = new(year: 2027,
                    month: 1,
                    day: 25);
                AssertPeriod(period2,
                    start: start2,
                    endExclusive: endExclusive2);
            },
            period =>
            {
                var period2 = period;
                DateOnly start2 = new(year: 2027,
                    month: 5,
                    day: 1);
                DateOnly endExclusive2 = new(year: 2027,
                    month: 5,
                    day: 2);
                AssertPeriod(period2,
                    start: start2,
                    endExclusive: endExclusive2);
            },
            period =>
            {
                var period2 = period;
                DateOnly start2 = new(year: 2027,
                    month: 5,
                    day: 2);
                DateOnly endExclusive2 = new(year: 2027,
                    month: 5,
                    day: 11);
                AssertPeriod(period2,
                    start: start2,
                    endExclusive: endExclusive2);
            });

        void AssertRange(
            int grade,
            Semester semester,
            DateOnly start,
            DateOnly endInclusive)
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

            Assert.Equal(expected: start, actual: actual.Start);
            Assert.Equal(expected: endInclusive, actual: actual.EndInclusive);
        }

        static void AssertPeriod(
            HolidayPeriod period,
            DateOnly start,
            DateOnly endExclusive)
        {
            Assert.Equal(expected: start, actual: period.Start);
            Assert.Equal(expected: endExclusive, actual: period.EndExclusive);
        }
    }

    [Fact]
    public void DateToEventTransformTest()
    {
        DateOnly Date(int day)
        {
            return new(year: 2025,
                month: 9,
                day: day);
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
                ],
                interval: 3)
            .ToArray();
        Assert.Collection(result,
            e1 =>
            {
                var first = Date(1);
                int count2 = 4;
                int interval2 = 3;
                var ev = Event.CreateRecurring(first,
                    count: count2,
                    interval: interval2);
                Assert.Equal(expected: ev, actual: e1);
            },
            e2 =>
            {
                var date = Date(11);
                var ev = Event.CreateSingle(date);
                Assert.Equal(expected: ev, actual: e2);
            },
            e2 =>
            {
                var first = Date(12);
                int count2 = 3;
                int interval2 = 3;
                var ev = Event.CreateRecurring(first,
                    count: count2,
                    interval: interval2);
                Assert.Equal(expected: ev, actual: e2);
            });
    }
}

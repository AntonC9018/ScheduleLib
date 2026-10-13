using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.ParserTests;

public sealed class AttendanceModeTests
{
    [Theory]
    [InlineData(AttendanceMode.Zi, true, false, false)]
    [InlineData(AttendanceMode.FrecventaRedusa, false, true, false)]
    [InlineData(AttendanceMode.DualZi, true, false, true)]
    [InlineData(AttendanceMode.DualFr, false, true, true)]
    [InlineData(AttendanceMode.Invalid, false, false, false)]
    [InlineData(AttendanceMode.Count, false, false, false)]
    public void AttendanceFormAndDualModalityAreIndependent(AttendanceMode mode, bool zi, bool fr, bool dual)
    {
        Assert.Equal(zi, mode.IsZi());
        Assert.Equal(fr, mode.IsFr());
        Assert.Equal(dual, mode.IsDual());
    }

    [Fact]
    public void DualZiKeepsTheExistingNumericIdentity()
    {
        Assert.Equal(2, (int)AttendanceMode.DualZi);
    }

    [Fact]
    public void AttendanceFlagsDoNotConfuseDualWithReducedAttendance()
    {
        var flags = AttendanceModeFlags.Zi | AttendanceModeFlags.DualFr;
        Assert.True(flags.Has(AttendanceMode.Zi));
        Assert.True(flags.Has(AttendanceMode.DualFr));
        Assert.False(flags.Has(AttendanceMode.FrecventaRedusa));
        Assert.False(flags.Has(AttendanceMode.DualZi));
        Assert.False(flags.Has(AttendanceMode.Invalid));
        Assert.False(flags.Has(AttendanceMode.Count));
        Assert.True(AttendanceModeFlags.DualZi.Has(AttendanceMode.DualZi));
        Assert.False(AttendanceModeFlags.FrecventaRedusa.Has(AttendanceMode.DualZi));
    }

    [Fact]
    public void ModeSpecificValuesHaveIndependentDualSlots()
    {
        var values = new OneForEachAttendanceMode<int> { Zi = 10, FrecventaRedusa = 20 };
        Assert.Equal(0, values[AttendanceMode.DualZi]);
        Assert.Equal(0, values[AttendanceMode.DualFr]);
        values[AttendanceMode.DualZi] = 30;
        values[AttendanceMode.DualFr] = 40;
        Assert.Equal(10, values[AttendanceMode.Zi]);
        Assert.Equal(20, values[AttendanceMode.FrecventaRedusa]);
        Assert.Equal(30, values[AttendanceMode.DualZi]);
        Assert.Equal(40, values[AttendanceMode.DualFr]);
    }

    [Theory]
    [InlineData("IA2601", "DU-IA2601", true)]
    [InlineData("DU-IA2601", "IA2601", true)]
    [InlineData("IAFR2601", "DU-IAFR2601", true)]
    [InlineData("DU-IAFR2601", "IAFR2601", true)]
    [InlineData("DU-IA2601", "DU-IAFR2601", false)]
    [InlineData("IA2601", "DU-IAFR2601", false)]
    public void SharedLessonsRequireTheSameAttendanceForm(string first, string second, bool valid)
    {
        var builder = new ScheduleBuilder
        {
            GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) }),
        };
        builder.EnableLookupModule();
        var lesson = builder.RegularLesson();
        lesson.Course(builder.Course("Course"));
        lesson.Groups([builder.Group(first), builder.Group(second)]);
        lesson.TimeSlot(new(0));
        lesson.DayOfWeek(DayOfWeek.Monday);
        if (valid)
            Assert.Single(builder.Build().WeeklyLessons);
        else
            Assert.Throws<InvalidLessonGroupsException>(() => builder.Build());
    }
}

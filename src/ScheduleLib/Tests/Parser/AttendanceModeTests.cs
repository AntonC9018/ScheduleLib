using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.ParserTests;

public sealed class AttendanceModeTests
{
    [Theory]
    [InlineData(AttendanceMode.Zi,
        true,
        false,
        false)]
    [InlineData(AttendanceMode.FrecventaRedusa,
        false,
        true,
        false)]
    [InlineData(AttendanceMode.DualZi,
        true,
        false,
        true)]
    [InlineData(AttendanceMode.DualFr,
        false,
        true,
        true)]
    [InlineData(AttendanceMode.Invalid,
        false,
        false,
        false)]
    [InlineData(AttendanceMode.Count,
        false,
        false,
        false)]
    public void AttendanceFormAndDualModalityAreIndependent(
        AttendanceMode mode,
        bool zi,
        bool fr,
        bool dual)
    {
        Assert.Equal(expected: zi, actual: mode.IsZi());
        Assert.Equal(expected: fr, actual: mode.IsFr());
        Assert.Equal(expected: dual, actual: mode.IsDual());
    }

    [Fact]
    public void DualZiKeepsTheExistingNumericIdentity()
    {
        Assert.Equal(expected: 2, actual: (int) AttendanceMode.DualZi);
    }

    [Fact]
    public void AttendanceFlagsDoNotConfuseDualWithReducedAttendance()
    {
        var flags = AttendanceModeFlags.Zi | AttendanceModeFlags.DualFr;
        bool condition = flags.Has(AttendanceMode.Zi);
        Assert.True(condition);
        bool condition2 = flags.Has(AttendanceMode.DualFr);
        Assert.True(condition2);
        bool condition3 = flags.Has(AttendanceMode.FrecventaRedusa);
        Assert.False(condition3);
        bool condition4 = flags.Has(AttendanceMode.DualZi);
        Assert.False(condition4);
        bool condition5 = flags.Has(AttendanceMode.Invalid);
        Assert.False(condition5);
        bool condition6 = flags.Has(AttendanceMode.Count);
        Assert.False(condition6);
        bool condition7 = AttendanceModeFlags.DualZi.Has(AttendanceMode.DualZi);
        Assert.True(condition7);
        bool condition8 = AttendanceModeFlags.FrecventaRedusa.Has(AttendanceMode.DualZi);
        Assert.False(condition8);
    }

    [Fact]
    public void ModeSpecificValuesHaveIndependentDualSlots()
    {
        var values = new OneForEachAttendanceMode<int>
        {
            Zi = 10,
            FrecventaRedusa = 20,
        };
        Assert.Equal(expected: 0, actual: values[AttendanceMode.DualZi]);
        Assert.Equal(expected: 0, actual: values[AttendanceMode.DualFr]);
        values[AttendanceMode.DualZi] = 30;
        values[AttendanceMode.DualFr] = 40;
        Assert.Equal(expected: 10, actual: values[AttendanceMode.Zi]);
        Assert.Equal(expected: 20, actual: values[AttendanceMode.FrecventaRedusa]);
        Assert.Equal(expected: 30, actual: values[AttendanceMode.DualZi]);
        Assert.Equal(expected: 40, actual: values[AttendanceMode.DualFr]);
    }

    [Theory]
    [InlineData("IA2601",
        "DU-IA2601",
        true)]
    [InlineData("DU-IA2601",
        "IA2601",
        true)]
    [InlineData("IAFR2601",
        "DU-IAFR2601",
        true)]
    [InlineData("DU-IAFR2601",
        "IAFR2601",
        true)]
    [InlineData("DU-IA2601",
        "DU-IAFR2601",
        false)]
    [InlineData("IA2601",
        "DU-IAFR2601",
        false)]
    public void SharedLessonsRequireTheSameAttendanceForm(
        string first,
        string second,
        bool valid)
    {
        var builder = new ScheduleBuilder
        {
            GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) }),
        };
        builder.EnableLookupModule();
        var lesson = builder.RegularLesson();
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel>
            lessonBuilder = lesson;
        global::ScheduleLib.CourseId course = builder.Course("Course");
        lessonBuilder.Course(course);
        lesson.Groups([builder.Group(first), builder.Group(second)]);
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel>
            lessonBuilder2 = lesson;
        global::TimeSlot timeSlot = new(0);
        lessonBuilder2.TimeSlot(timeSlot);
        lesson.DayOfWeek(DayOfWeek.Monday);
        if (valid)
        {
            Assert.Single(builder.Build().WeeklyLessons);
        }
        else
        {
            Assert.Throws<InvalidLessonGroupsException.MixedAttendanceModesException>(() => builder.Build());
        }
    }
}

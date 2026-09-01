using ScheduleLib.Builders;

namespace ScheduleLib.ParserTests;

public sealed class ScheduleSanityChecksTests
{
    [Fact]
    public void RejectsNonNumericNonSpecialSubGroup()
    {
        var schedule = new ScheduleBuilder();
        schedule.RegularLesson().SubGroup(new("IA2504"));

        var error = Assert.Throws<InvalidOperationException>(() => schedule.SanityChecks());

        Assert.Contains("IA2504", error.Message);
    }

    [Fact]
    public void AcceptsAllNumericAndSpecialSubGroups()
    {
        var schedule = new ScheduleBuilder();
        schedule.RegularLesson().SubGroup(SubGroup.All);
        foreach (var special in SpecialSubGroups.AllSpecial)
        {
            schedule.RegularLesson().SubGroup(special);
        }
        for (int number = 1; number <= 10; number++)
        {
            schedule.RegularLesson().SubGroup(SubGroup.CreateNumeric(number));
        }

        schedule.SanityChecks();
    }

    [Fact]
    public void SkipsSubGroupValidationWhenDisabled()
    {
        var schedule = new ScheduleBuilder();
        schedule.ValidationSettings.SubGroup = SubGroupValidationMode.None;
        schedule.RegularLesson().SubGroup(new("unconfigured"));

        schedule.SanityChecks();
    }
}

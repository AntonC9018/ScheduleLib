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
    public void RejectsSpecializationLabelsAsSubGroups()
    {
        var schedule = new ScheduleBuilder();
        schedule.RegularLesson().SubGroup(new(Specializations.CV.Value!));

        var error = Assert.Throws<InvalidOperationException>(() => schedule.SanityChecks());

        Assert.Contains("CV", error.Message);
    }

    [Fact]
    public void ClassifiesSpecializationLabelsIntoSpecialization()
    {
        var schedule = new ScheduleBuilder();
        var lesson = schedule.RegularLesson();
        lesson.SubGroup(new("CV"));

        schedule.ClassifySubGroups();

        Assert.Equal(SubGroup.All, lesson.Model.Base.Group.SubGroup);
        Assert.Equal(Specializations.CV, lesson.Model.Base.Group.Specialization);
    }

    [Fact]
    public void NormalizesBeginnerCounterpartsToNonBeginners()
    {
        var schedule = new ScheduleBuilder();
        var groupId = schedule.Group("IA2401").Id;
        var courseId = schedule.Course("Limba engleză");
        var beginner = schedule.RegularLesson();
        beginner.Course(courseId);
        beginner.Group(groupId);
        beginner.SubGroup(SpecialSubGroups.Beginners);
        var counterpart = schedule.RegularLesson();
        counterpart.Course(courseId);
        counterpart.Group(groupId);

        schedule.NormalizeLanguageProficiency();

        Assert.Equal(SpecialSubGroups.NonBeginners, counterpart.Model.Base.Group.SubGroup);
        Assert.Equal(SpecialSubGroups.Beginners, beginner.Model.Base.Group.SubGroup);
    }

    [Fact]
    public void NormalizationIsIdempotentOverAlreadyNormalizedData()
    {
        var schedule = new ScheduleBuilder();
        var groupId = schedule.Group("IA2401").Id;
        var courseId = schedule.Course("Limba engleză");
        var beginner = schedule.RegularLesson();
        beginner.Course(courseId);
        beginner.Group(groupId);
        beginner.SubGroup(SpecialSubGroups.Beginners);
        var nonBeginner = schedule.RegularLesson();
        nonBeginner.Course(courseId);
        nonBeginner.Group(groupId);
        nonBeginner.SubGroup(SpecialSubGroups.NonBeginners);

        schedule.NormalizeLanguageProficiency();

        Assert.Equal(SpecialSubGroups.NonBeginners, nonBeginner.Model.Base.Group.SubGroup);
    }

    [Fact]
    public void BeginnerWithoutCounterpartFails()
    {
        var schedule = new ScheduleBuilder();
        var groupId = schedule.Group("IA2401").Id;
        var courseId = schedule.Course("Limba engleză");
        var beginner = schedule.RegularLesson();
        beginner.Course(courseId);
        beginner.Group(groupId);
        beginner.SubGroup(SpecialSubGroups.Beginners);

        var error = Assert.Throws<InvalidOperationException>(
            () => schedule.NormalizeLanguageProficiency());

        Assert.Contains("counterpart", error.Message);
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

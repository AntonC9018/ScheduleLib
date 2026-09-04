using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class ScheduleSanityChecksTests
{
    [Fact]
    public void RejectsNonNumericNonSpecialSubGroup()
    {
        var schedule = new ScheduleBuilder();
        var group = schedule.Group("IA2401").Id;
        var lesson = schedule.RegularLesson();
        lesson.Group(group);
        lesson.SubGroup(new("IA2504"));

        var error = Assert.Throws<InvalidOperationException>(() => schedule.SanityChecks());

        Assert.Contains("IA2504", error.Message);
        Assert.Contains("IA2401", error.Message);
        Assert.Contains("Specialization registry context", error.Message);
        Assert.Contains("no specialization registry configured", error.Message);
        Assert.Contains("configured subgroup selector", error.Message);
    }

    [Fact]
    public void UnknownSubGroupReportsTheMatchingRegistrySelector()
    {
        var registryBuilder = new SpecializationRegistryBuilder();
        registryBuilder.Set([Specializations.CV]).ApplyTo(_ => { });

        var schedule = new ScheduleBuilder
        {
            SpecializationRegistry = registryBuilder.Build(),
        };
        var group = schedule.Group("IA2401").Id;
        var lesson = schedule.RegularLesson();
        lesson.Group(group);
        lesson.SubGroup(new("unconfigured"));

        var error = Assert.Throws<InvalidOperationException>(() => schedule.SanityChecks());

        Assert.Contains("(any group) => [CV]", error.Message);
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
    public void NormalizationKeepsTheCounterpartSpecialization()
    {
        var schedule = new ScheduleBuilder();
        var groupId = schedule.Group("IA2401").Id;
        var courseId = schedule.Course("Limba engleză");
        var beginner = schedule.RegularLesson();
        beginner.Course(courseId);
        beginner.Group(groupId);
        beginner.SubGroup(SpecialSubGroups.Beginners);
        beginner.Specialization(Specializations.CV);
        var counterpart = schedule.RegularLesson();
        counterpart.Course(courseId);
        counterpart.Group(groupId);
        counterpart.Specialization(Specializations.CV);

        schedule.NormalizeLanguageProficiency();

        Assert.Equal(SpecialSubGroups.NonBeginners, counterpart.Model.Base.Group.SubGroup);
        Assert.Equal(Specializations.CV, counterpart.Model.Base.Group.Specialization);
    }

    [Fact]
    public void NormalizationLeavesNumericAndLanguageLessonsUnchanged()
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
        var numeric = schedule.RegularLesson();
        numeric.Course(courseId);
        numeric.Group(groupId);
        numeric.SubGroup(SubGroup.CreateNumeric(1));
        var language = schedule.RegularLesson();
        language.Course(courseId);
        language.Group(groupId);
        language.SubGroup(SpecialSubGroups.Ro);

        schedule.NormalizeLanguageProficiency();

        Assert.Equal(SpecialSubGroups.NonBeginners, counterpart.Model.Base.Group.SubGroup);
        Assert.Equal(SubGroup.CreateNumeric(1), numeric.Model.Base.Group.SubGroup);
        Assert.Equal(SpecialSubGroups.Ro, language.Model.Base.Group.SubGroup);
    }

    [Fact]
    public void SharedCounterpartCannotMixGroupsWithAndWithoutTheBeginnerSplit()
    {
        var schedule = new ScheduleBuilder();
        var firstGroup = schedule.Group("IA2401").Id;
        var secondGroup = schedule.Group("IA2402").Id;
        var course = schedule.Course("Limba engleză");
        var beginner = schedule.RegularLesson();
        beginner.Course(course);
        beginner.Group(firstGroup);
        beginner.SubGroup(SpecialSubGroups.Beginners);
        var sharedCounterpart = schedule.RegularLesson();
        sharedCounterpart.Course(course);
        sharedCounterpart.Groups([firstGroup, secondGroup]);

        var error = Assert.Throws<InconsistentLanguageSplitException>(
            () => schedule.NormalizeLanguageProficiency());

        Assert.Contains("mixes groups", error.Message);
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

        var error = Assert.Throws<InconsistentLanguageSplitException>(
            () => schedule.NormalizeLanguageProficiency());

        Assert.Contains("counterpart", error.Message);
    }

    [Fact]
    public void SourceClassificationAcceptsOneSubGroupAndOneSpecialization()
    {
        var (context, group) = CreateDocContext();
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        context.SetCommonProps(lesson, Parsed(groupName: "Spring", subGroup: "I"));

        Assert.Equal(SubGroup.CreateNumeric(1), lesson.Model.Base.Group.SubGroup);
        Assert.Equal(Specializations.Spring, lesson.Model.Base.Group.Specialization);
    }

    [Fact]
    public void SourceClassificationRejectsTwoSubGroups()
    {
        var (context, group) = CreateDocContext();
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        var error = Assert.Throws<InvalidOperationException>(
            () => context.SetCommonProps(lesson, Parsed(groupName: "ro", subGroup: "ru")));

        Assert.Contains("two subgroups", error.Message);
    }

    [Fact]
    public void SourceClassificationRejectsTwoSpecializations()
    {
        var (context, group) = CreateDocContext();
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        var error = Assert.Throws<InvalidOperationException>(
            () => context.SetCommonProps(lesson, Parsed(groupName: "Spring", subGroup: "CV")));

        Assert.Contains("two specializations", error.Message);
    }

    [Fact]
    public void SourceClassificationUsesConfiguredRegistrySpecializations()
    {
        var future = new Specialization("Future track");
        var registryBuilder = new SpecializationRegistryBuilder();
        registryBuilder.Set([future]).ApplyTo(_ => { });
        var (context, group) = CreateDocContext(registryBuilder.Build());
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        context.SetCommonProps(lesson, Parsed(groupName: future.Value!, subGroup: "I"));

        Assert.Equal(SubGroup.CreateNumeric(1), lesson.Model.Base.Group.SubGroup);
        Assert.Equal(future, lesson.Model.Base.Group.Specialization);
    }

    [Fact]
    public void SkipsSubGroupValidationWhenDisabled()
    {
        var schedule = new ScheduleBuilder();
        schedule.ValidationSettings.SubGroup = SubGroupValidationMode.None;
        schedule.RegularLesson().SubGroup(new("unconfigured"));

        schedule.SanityChecks();
    }

    private static (DocParseContext Context, GroupId Group) CreateDocContext(
        SpecializationRegistry? specializationRegistry = null)
    {
        var context = DocParseContext.Create(new()
        {
            DayNameProvider = new DayNameProvider(),
            CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new()
            {
                ProcessSpacesCourseName = Config.WhiteSpaceActionCourseName,
            }),
            SpecializationRegistry = specializationRegistry,
        });
        var group = context.Schedule.Group("IA2401").Id;
        return (context, group);
    }

    private static ParsedLesson Parsed(string groupName, string subGroup)
    {
        return new()
        {
            LessonName = "Course".AsMemory(),
            TeacherNames = [],
            RoomName = default,
            GroupName = groupName.AsMemory(),
            SubGroup = subGroup.AsMemory(),
        };
    }
}

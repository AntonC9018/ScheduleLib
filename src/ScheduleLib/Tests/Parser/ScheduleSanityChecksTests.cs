using ScheduleLib.Parsing;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class ScheduleSanityChecksTests
{
    /// <summary>A schedule import context together with the group it created.</summary>
    private readonly record struct DocContext(ScheduleImportContext Context, GroupId Group);

    [Fact]
    public void RejectsNonNumericNonSpecialSubGroup()
    {
        var schedule = new ScheduleBuilder();
        var group = schedule.Group("IA2401").Id;
        var lesson = schedule.RegularLesson();
        lesson.Group(group);
        var
            lessonBuilder = lesson;
        SubGroup subGroup = new("IA2504");
        lessonBuilder.SubGroup(subGroup);

        var error = Assert.Throws<UnknownSubGroupException>(() => schedule.SanityChecks());

        Assert.Contains(expectedSubstring: "IA2504", actualString: error.Message);
        Assert.Contains(expectedSubstring: "IA2401", actualString: error.Message);
        Assert.Contains(expectedSubstring: "Specialization registry context", actualString: error.Message);
        Assert.Contains(expectedSubstring: "no specialization registry configured", actualString: error.Message);
        Assert.Contains(expectedSubstring: "configured subgroup selector", actualString: error.Message);
    }

    [Fact]
    public void UnknownSubGroupReportsTheMatchingRegistrySelector()
    {
        var registryBuilder = new SpecializationRegistryBuilder();
        registryBuilder.Set([Specialization.CV])
            .ApplyTo(_ => { });

        var schedule = new ScheduleBuilder
        {
            SpecializationRegistry = registryBuilder.Build(),
        };
        var group = schedule.Group("IA2401").Id;
        var lesson = schedule.RegularLesson();
        lesson.Group(group);
        var
            lessonBuilder = lesson;
        SubGroup subGroup = new("unconfigured");
        lessonBuilder.SubGroup(subGroup);

        var error = Assert.Throws<UnknownSubGroupException>(() => schedule.SanityChecks());

        Assert.Contains(expectedSubstring: "(any group) => [CV]", actualString: error.Message);
    }

    [Fact]
    public void AcceptsAllNumericAndSpecialSubGroups()
    {
        var schedule = new ScheduleBuilder();
        schedule.RegularLesson()
            .SubGroup(SubGroup.All);
        foreach (var special in SpecialSubGroups.AllSpecial)
        {
            schedule.RegularLesson()
                .SubGroup(special);
        }
        for (int number = 1; number <= 10; number++)
        {
            var
                regularLesson = schedule.RegularLesson();
            var subGroup = SubGroup.CreateNumeric(number);
            regularLesson.SubGroup(subGroup);
        }

        schedule.SanityChecks();
    }

    [Fact]
    public void RejectsSpecializationLabelsAsSubGroups()
    {
        var schedule = new ScheduleBuilder();
        var
            regularLesson = schedule.RegularLesson();
        SubGroup subGroup = new(Specialization.CV.Value!);
        regularLesson.SubGroup(subGroup);

        var error = Assert.Throws<UnknownSubGroupException>(() => schedule.SanityChecks());

        Assert.Contains(expectedSubstring: "CV", actualString: error.Message);
    }

    [Fact]
    public void ClassifiesSpecializationLabelsIntoSpecialization()
    {
        var schedule = new ScheduleBuilder();
        var lesson = schedule.RegularLesson();
        var
            lessonBuilder = lesson;
        SubGroup subGroup = new("CV");
        lessonBuilder.SubGroup(subGroup);

        schedule.ClassifySubGroups();

        Assert.Equal(expected: SubGroup.All, actual: lesson.Model.Base.Group.SubGroup);
        Assert.Equal(expected: Specialization.CV, actual: lesson.Model.Base.Group.Specialization);
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

        Assert.Equal(expected: SpecialSubGroups.NonBeginners, actual: counterpart.Model.Base.Group.SubGroup);
        Assert.Equal(expected: SpecialSubGroups.Beginners, actual: beginner.Model.Base.Group.SubGroup);
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

        Assert.Equal(expected: SpecialSubGroups.NonBeginners, actual: nonBeginner.Model.Base.Group.SubGroup);
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
        beginner.Specialization(Specialization.CV);
        var counterpart = schedule.RegularLesson();
        counterpart.Course(courseId);
        counterpart.Group(groupId);
        counterpart.Specialization(Specialization.CV);

        schedule.NormalizeLanguageProficiency();

        Assert.Equal(expected: SpecialSubGroups.NonBeginners, actual: counterpart.Model.Base.Group.SubGroup);
        Assert.Equal(expected: Specialization.CV, actual: counterpart.Model.Base.Group.Specialization);
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
        var
            lessonBuilder = numeric;
        var subGroup = SubGroup.CreateNumeric(1);
        lessonBuilder.SubGroup(subGroup);
        var language = schedule.RegularLesson();
        language.Course(courseId);
        language.Group(groupId);
        language.SubGroup(SpecialSubGroups.Ro);

        schedule.NormalizeLanguageProficiency();

        Assert.Equal(expected: SpecialSubGroups.NonBeginners, actual: counterpart.Model.Base.Group.SubGroup);
        var expected2 = SubGroup.CreateNumeric(1);
        var actual2 = numeric.Model.Base.Group.SubGroup;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: SpecialSubGroups.Ro, actual: language.Model.Base.Group.SubGroup);
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

        var error = Assert.Throws<InconsistentLanguageSplitException.MixedSplitException>(() =>
            schedule.NormalizeLanguageProficiency());

        Assert.Contains(expectedSubstring: "mixes groups", actualString: error.Message);
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

        var error = Assert.Throws<InconsistentLanguageSplitException.MissingCounterpartException>(() =>
            schedule.NormalizeLanguageProficiency());

        Assert.Contains(expectedSubstring: "counterpart", actualString: error.Message);
    }

    [Fact]
    public void SourceClassificationAcceptsOneSubGroupAndOneSpecialization()
    {
        var (context, group) = CreateDocContext();
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        ParsedLesson CalculateParsedLesson()
        {
            var parsedLesson = Parsed(groupName: "Spring", subGroup: "I");
            return parsedLesson;
        }

        context.SetCommonProps(lesson, CalculateParsedLesson());

        var expected2 = SubGroup.CreateNumeric(1);
        var actual2 = lesson.Model.Base.Group.SubGroup;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: Specialization.Spring, actual: lesson.Model.Base.Group.Specialization);
    }

    [Fact]
    public void SourceClassificationRejectsTwoSubGroups()
    {
        var (context, group) = CreateDocContext();
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        var error = Assert.Throws<ConflictingSubGroupException>(() =>
        {
            ParsedLesson CalculateParsedLesson()
            {
                var parsedLesson = Parsed(groupName: "ro", subGroup: "ru");
                return parsedLesson;
            }

            return context.SetCommonProps(lesson, CalculateParsedLesson());
        });

        Assert.Contains(expectedSubstring: "two subgroups", actualString: error.Message);
    }

    [Fact]
    public void SourceClassificationRejectsTwoSpecializations()
    {
        var (context, group) = CreateDocContext();
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        var error = Assert.Throws<ConflictingSpecializationException>(() =>
        {
            ParsedLesson CalculateParsedLesson()
            {
                var parsedLesson =
                    Parsed(groupName: "Spring", subGroup: "CV");
                return parsedLesson;
            }

            return context.SetCommonProps(lesson, CalculateParsedLesson());
        });

        Assert.Contains(expectedSubstring: "two specializations", actualString: error.Message);
    }

    [Fact]
    public void SourceClassificationUsesConfiguredRegistrySpecializations()
    {
        var future = new Specialization("Future track");
        var registryBuilder = new SpecializationRegistryBuilder();
        registryBuilder.Set([future])
            .ApplyTo(_ => { });
        var (context, group) = CreateDocContext(registryBuilder.Build());
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        context.SetCommonProps(lesson, Parsed(groupName: future.Value!, subGroup: "I"));

        var expected2 = SubGroup.CreateNumeric(1);
        var actual2 = lesson.Model.Base.Group.SubGroup;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: future, actual: lesson.Model.Base.Group.Specialization);
    }

    [Fact]
    public void SourceClassificationUsesInjectedPrefixMatcher()
    {
        var future = new Specialization("Future track");
        var registryBuilder = new SpecializationRegistryBuilder();
        registryBuilder.Set([future])
            .ApplyTo(_ => { });
        // The default matcher knows nothing about "Future track", so the
        // abbreviated group name only classifies through the injected matcher.
        var matcher = new SubGroupPrefixMatcher([new SubGroup(future.Value!)]);
        var (context, group) = CreateDocContext(registryBuilder.Build(), matcher);
        var lesson = context.Schedule.RegularLesson();
        lesson.Group(group);

        ParsedLesson CalculateParsedLesson()
        {
            var parsedLesson = Parsed(groupName: "futu", subGroup: "I");
            return parsedLesson;
        }

        context.SetCommonProps(lesson, CalculateParsedLesson());

        var expected2 = SubGroup.CreateNumeric(1);
        var actual2 = lesson.Model.Base.Group.SubGroup;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: future, actual: lesson.Model.Base.Group.Specialization);
    }

    [Fact]
    public void SkipsSubGroupValidationWhenDisabled()
    {
        var schedule = new ScheduleBuilder();
        schedule.ValidationSettings.SubGroup = SubGroupValidationMode.None;
        var
            regularLesson = schedule.RegularLesson();
        SubGroup subGroup = new("unconfigured");
        regularLesson.SubGroup(subGroup);

        schedule.SanityChecks();
    }

    [Fact]
    public void DropsEngSubGroupFromEnglishGroups()
    {
        var schedule = new ScheduleBuilder();
        var englishGroup = schedule.Group("IA2401(en)").Id;
        var roGroup = schedule.Group("IA2402").Id;
        var courseId = schedule.Course("Limba engleză");
        var lesson = schedule.RegularLesson();
        lesson.Course(courseId);
        lesson.Group(englishGroup);
        lesson.SubGroup(SpecialSubGroups.Eng);
        var ruLesson = schedule.RegularLesson();
        ruLesson.Course(courseId);
        ruLesson.Group(roGroup);
        ruLesson.SubGroup(SpecialSubGroups.Ru);

        schedule.DropEngSubGroupFromEnglishGroups();

        Assert.Equal(expected: SubGroup.All, actual: lesson.Model.Base.Group.SubGroup);
        Assert.Equal(expected: SpecialSubGroups.Ru, actual: ruLesson.Model.Base.Group.SubGroup);
    }

    [Fact]
    public void KeepsEngSubGroupWhenGroupsAreNotAllEnglish()
    {
        var schedule = new ScheduleBuilder();
        var englishGroup = schedule.Group("IA2401(en)").Id;
        var roGroup = schedule.Group("IA2402").Id;
        var courseId = schedule.Course("Limba engleză");
        var lesson = schedule.RegularLesson();
        lesson.Course(courseId);
        lesson.Group(englishGroup);
        lesson.Group(roGroup);
        lesson.SubGroup(SpecialSubGroups.Eng);

        schedule.DropEngSubGroupFromEnglishGroups();

        Assert.Equal(expected: SpecialSubGroups.Eng, actual: lesson.Model.Base.Group.SubGroup);
    }

    private static DocContext CreateDocContext(
        SpecializationRegistry? specializationRegistry = null,
        SubGroupPrefixMatcher? subGroupMatcher = null)
    {
        var context = ScheduleImportContext.Create(new()
        {
            DayNameProvider = new DayNameProvider(),
            CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new()
            {
                ProcessSpacesCourseName = Config.WhiteSpaceActionCourseName,
            }),
            SpecializationRegistry = specializationRegistry,
            SubGroupMatcher = subGroupMatcher,
        });
        var group = context.Schedule.Group("IA2401").Id;
        return new(context, group);
    }

    private static ParsedLesson Parsed(string groupName, string subGroup)
    {
        return new()
        {
            LessonName = "Course".AsMemory(),
            TeacherNames = [],
            RoomName = default,
            GroupName = groupName.AsMemory(),
            PartitionHint = subGroup.AsMemory(),
        };
    }
}

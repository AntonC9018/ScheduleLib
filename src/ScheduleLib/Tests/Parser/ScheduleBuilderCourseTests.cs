using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Tests;

public sealed class ScheduleBuilderCourseTests
{
    [Fact]
    public void CourseRejectsDuplicateAliasesWithLookupEnabledWithoutMutatingCourses()
    {
        var builder = new ScheduleBuilder();
        builder.EnableLookupModule();
        builder.Course("Math", "Mathematics");

        int countBefore = builder.Courses.Count;
        Assert.Throws<DuplicateCourseNameException>(() => builder.Course("Algebra", "Mathematics"));
        Assert.Throws<DuplicateCourseNameException>(() => builder.Course("Physics", "physics"));
        Assert.Equal(expected: countBefore, actual: builder.Courses.Count);
    }

    [Fact]
    public void CourseRejectsDuplicateAliasesWithinSingleCallWithoutLookupModule()
    {
        var builder = new ScheduleBuilder();

        Assert.Throws<DuplicateCourseNameException>(() => builder.Course("Physics", "physics"));
        Assert.Equal(expected: 0, actual: builder.Courses.Count);
    }

    [Fact]
    public void EnableLookupModuleThrowsTypedDuplicateExceptions()
    {
        var courseBuilder = new ScheduleBuilder();
        courseBuilder.Course("Math", "Mathematics");
        courseBuilder.Course("Algebra", "Mathematics");

        var courseEx = Assert.Throws<DuplicateCourseNameException>(() => courseBuilder.EnableLookupModule());
        Assert.Equal(expected: "Mathematics", actual: courseEx.CourseName);

        var groupBuilder = new ScheduleBuilder
        {
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2025),
            }),
        };
        groupBuilder.Group("IA2401");
        groupBuilder.Group("IA2401");

        var groupEx = Assert.Throws<DuplicateGroupNameException>(() => groupBuilder.EnableLookupModule());
        Assert.Equal(expected: "IA2401", actual: groupEx.GroupName);
    }
}

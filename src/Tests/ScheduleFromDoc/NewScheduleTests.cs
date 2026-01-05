using ScheduleLib;
using ScheduleLib.Builders;

namespace ScheduleFromDoc.Tests;

public sealed class NewScheduleTests
{
    [Fact]
    public async Task NodeJsTest()
    {
        var helper = IntegrationTestHelper.CreateNew();
        using var cts = IntegrationTestHelper.CreateCts();
        var context = await helper.GetContextFromWord(cts.Token);
        var schedule = context.Schedule.Build();
        var lookup = context.Schedule.Lookup(context.CourseNameUnifierModule);
        var nodejsCourseId = lookup.Course("Node.js".AsMemory())!.Value;
        var lessons = lookup.LessonsOfCourse(nodejsCourseId);
        var verifyModel = lessons
            .Select(x => schedule.Get(x))
            .Where(x =>
            {
                if (x.Weekly is { } w)
                {
                    return w.Date.Period == schedule.LatestPeriodId();
                }
                return true;
            })
            .Select(x =>
            {
                var groups = x.Lesson.Groups;
                var groupNames = groups.Select(g => schedule.Get(g).Name);
                var teacher = x.Lesson.Teachers[0];
                var teacherName = schedule.Get(teacher).PersonName.ToString();
                return new
                {
                    Groups = groupNames,
                    Teacher = teacherName,
                };
            })
            .ToArray();
        await Verify(verifyModel);
    }
}

using MainCli;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Builders;

namespace ScheduleFromDoc.Tests;

public sealed class NewScheduleTests
{
    [Fact]
    public async Task NodeJsTest()
    {
        using var helper = await IntegrationTestHelper.CreateNew();
        var schedule = helper.ServiceProvider.GetRequiredService<Schedule>();
        var lookup = helper.ServiceProvider.GetRequiredService<LookupFacade>();
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

using Microsoft.Extensions.DependencyInjection;
using ScheduleFromDoc.Tests;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Topics;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

namespace ScheduleLib.Topics.Tests;

public sealed class Test
{
    [Fact]
    public async Task TestStuff()
    {
        await TestLab(
            path: "topics/1_ok/manifest.json",
            courseName: "Programare în C++");
    }

    [Fact]
    public async Task FallbackAll()
    {
        // Topics with no language work as fallback
        // when no specific language was specified.
        await TestLab(
            path: "topics/2_default_all/manifest.json",
            courseName: "Programare în C++");
    }

    private async Task TestLab(string path, string courseName)
    {
        using var helper = await IntegrationTestHelper.CreateNew();
        var schedule = helper.GetScheduleFromSourceOfTruth();
        var lookup = helper.ServiceProvider.GetRequiredService<LookupFacade>();
        var courseId = lookup.Course(courseName.AsMemory())!.Value;

        var teachers = schedule.EnumerateWeeklyLessons()
            .Where(x => x.Lesson.Course == courseId)
            .Select(x => x.Lesson.Teachers)
            .SelectMany(x => x)
            .Distinct();

        var teacherId = teachers.First();
        var teacherName = schedule.Get(teacherId).PersonName.AsNameFields();
        var manifestFileSource = ActivatorUtilities.CreateInstance<ManifestFileSource>(
            helper.ServiceProvider,
            path,
            new Name(teacherName));

        var manifest = await manifestFileSource.Read(helper.CancellationToken);
        var filteredSchedule = schedule.Filter(
            new ScheduleFilter()
                .WithCourse(courseId)
                .WithTeacher(teacherId));

        var builder = ActivatorUtilities.CreateInstance<AllLessonTopicsDatabaseBuilder>(helper.ServiceProvider, filteredSchedule);
        await builder.AddFromManifest(manifest, lookup, helper.CancellationToken);
        var result = builder.Build();

        var list = new List<string>();
        int dayIndex = 0;
        var groups = new FoundGroups
        {
            Value = new(),
            IsWildcard = false,
        };
        while (true)
        {
            var topic = result.Get(new(
                courseId: courseId,
                groups: groups,
                groupSplit: GroupSplitKey.All,
                lessonType: LessonType.Lab,
                dayIndex: dayIndex,
                dateTime: default));
            if (topic == null)
            {
                break;
            }
            list.Add(topic);
            dayIndex++;
        }

        await Verify(list);
    }

}

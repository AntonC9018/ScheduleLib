using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;

namespace Tests.ScheduleCommon;

public static class ScheduleTestHelper
{
    private static readonly CourseNameUnifierConfig PlainCourseNames = CourseNameUnifierConfig.Create(
        new CourseNameParserConfig(new()),
        []);

    /// <summary>Without a course name configuration, names only match exactly or after parsing.</summary>
    public static LookupFacade CreateLookup(
        this ScheduleBuilder builder,
        Schedule schedule,
        CourseNameUnifierConfig? courseNames = null)
    {
        var lookup = schedule.CreateScheduleLookup(courseNames);
        var groupParseContext = builder.GroupParseContext
            ?? GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2025),
            });
        return new LookupFacade(
            schedule: schedule,
            lookup: lookup,
            remappings: builder.Remappings,
            groupParseContext: groupParseContext);
    }

    public static ScheduleLookup CreateScheduleLookup(
        this Schedule schedule,
        CourseNameUnifierConfig? courseNames = null)
    {
        return ScheduleLookup.Create(schedule, courseNames ?? PlainCourseNames);
    }

    public static StudyYear StudyYear(this ScheduleBuilder s)
    {
        return s.GroupParseContext!.CurrentStudyYear;
    }

    public static void SetStudyYear(this ScheduleBuilder s, int year)
    {
        if (s.Groups.Count != 0)
        {
            throw new InvalidOperationException("The year must be initialized prior to creating groups.");
        }
        s.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(year),
        });
    }

    public const string TestSchedulePath = "data/schedule_2025_1.json";

    public static async Task<ScheduleBuilder> CreateTestSchedule()
    {
        using var cts = TestAnyLessonSchedulingDateHelper.CreateCts();
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2025);
        await using var scheduleJson = File.OpenRead(TestSchedulePath);
        var model = await ScheduleSerializer.Deserialize(scheduleJson, cts.Token);
        ScheduleSerializer.AddToBuilder(builder, model);
        return builder;
    }

    public static FilteredSchedule FilterForTeacher(
        Schedule schedule,
        LookupFacade lookup,
        TeacherBuilderModel.NameModel name)
    {
        var teacherId = lookup.Teacher(name)!.Value;
        var filteredSchedule = schedule.Filter(new()
        {
            TeacherFilter =
            {
                IncludeIds = [teacherId],
            },
            PeriodFilter =
            {
                PeriodId = schedule.LatestPeriodId(),
            },
        });
        return filteredSchedule;
    }

    public static async Task<FilteredSchedule> CreateScheduleForTeacher(TeacherBuilderModel.NameModel name)
    {
        var s = await CreateTestSchedule();
        var schedule = s.Build();
        var ret = FilterForTeacher(
            schedule,
            s.CreateLookup(schedule),
            name);
        return ret;
    }
}

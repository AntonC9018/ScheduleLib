using MainCli.JsonWebsite;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;

namespace JsonWebsiteSchedule.Tests;

public sealed class JsonWebsiteScheduleTests
{
    [Fact]
    public async Task CountTest()
    {
        var schedule = await CreateScheduleForTeacher();
        var model = WebsiteJsonScheduleHelper.CreateSerializationModel(schedule, new()
        {
            ParityDisplay = new(),
            LessonTypeDisplay = new(),
            SubGroupNumberDisplay = new(),
        });
        var stream = new MemoryStream();
        await WebsiteJsonScheduleHelper.Serialize(model, stream);
        stream.Position = 0;
        // ReSharper disable once MethodHasAsyncOverload
        using var reader = new StreamReader(stream);
        // ReSharper disable once MethodHasAsyncOverload
        var str = reader.ReadToEnd();
        await Verify(new Target("json", str));
    }

    private static async Task<FilteredSchedule> CreateScheduleForTeacher()
    {
        var s = await CreateSchedule();
        s.EnableLookupModule();
        var schedule = s.Build();
        var teacherId = s.Lookup().Teacher("Titu", "Capcelea")!.Value;
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

    // TODO: Common code and file, move to common project.
    private static async Task<ScheduleBuilder> CreateSchedule()
    {
        using var cts = TestHelper.CreateCts();
        var builder = new ScheduleBuilder();
        builder.SetStudyYear(2025);
        await using var scheduleJson = File.OpenRead("data/schedule_2025_1.json");
        var model = await ScheduleSerializer.Deserialize(scheduleJson, cts.Token);
        ScheduleSerializer.AddToBuilder(builder, model);
        return builder;
    }
}

using MainCli;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleFromDoc.Tests;

public sealed class IntegrationTestHelper
{
    public const string VerifyScheduleSnapshotName = "verify_schedule_model";
    public const string ScheduleJsonSnapshotName = "verify_schedule_json";
    public const string ScheduleSnapshotJsonPath = $"{ScheduleJsonSnapshotName}.verified.json";

    public readonly int Year;
    public readonly Semester Semester;

    public IntegrationTestHelper(
        int year,
        Semester sem)
    {
        Year = year;
        Semester = sem;
    }

    public static IntegrationTestHelper CreateDefault()
    {
        return new(2024, Semester.Sem2);
    }

    public static IntegrationTestHelper CreateNew()
    {
        return new(2025, Semester.Sem1);
    }

    public static CancellationTokenSource CreateCts()
    {
        return TestHelper.CreateCts();
    }

    public async Task<DocParseContext> GetContextFromWord(CancellationToken cancellationToken)
    {
        var context = DocParseContext.Create(new()
        {
            DayNameProvider = new(),
            CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new()
            {
                ProcessSpacesCourseName = Config.WhiteSpaceActionCourseName,
            }),
        });

        context.Schedule.SetStudyYear(Year);

        string dirName = @$"data\{Year}_sem{Semester.AsOrdinal()}";
        await TasksHelper.ParseDocumentDirIntoSchedule(
            context,
            dirName,
            cancellationToken: cancellationToken);

        return context;
    }

    public async Task<Schedule> GetScheduleFromWord(CancellationToken cancellationToken)
    {
        var context = await GetContextFromWord(cancellationToken);
        var schedule = context.Schedule.Build();
        return schedule;
    }

    public static async Task AddScheduleToBuilder(
        ScheduleBuilder builder,
        string jsonPath,
        CancellationToken cancellationToken)
    {
        await using var reader = File.OpenRead(jsonPath);
        var scheduleModel = await ScheduleSerializer.Deserialize(reader, cancellationToken);
        ScheduleSerializer.AddToBuilder(builder, scheduleModel);
    }

    public static async Task<Schedule> GetScheduleFromJson(string jsonPath, CancellationToken cancellationToken)
    {
        var scheduleBuilder = new ScheduleBuilder();
        await AddScheduleToBuilder(scheduleBuilder, jsonPath, cancellationToken);
        var jsonSchedule = scheduleBuilder.Build();
        return jsonSchedule;
    }
}

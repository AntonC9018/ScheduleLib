using MainCli;
using MainCli.FR;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleFromDoc.Tests;

public enum TestOption
{
    Default,
    New,
}

// TODO: Move context to service provider.
public sealed class IntegrationTestHelper
{
    public static IEnumerable<object[]> TestOptionMemberData => [
        [TestOption.Default],
        [TestOption.New],
    ];
    public static string VerifyScheduleSnapshotName(TestOption option) => $"{Enum.GetName(option)}_verify_schedule_model";
    public static string ScheduleJsonSnapshotName(TestOption option) => $"{Enum.GetName(option)}_verify_schedule_json";
    public static string ScheduleSnapshotJsonPath(TestOption option) => $"{ScheduleJsonSnapshotName(option)}.verified.json";

    public readonly int Year;
    public readonly Semester Semester;

    public IntegrationTestHelper(
        int year,
        Semester sem)
    {
        Year = year;
        Semester = sem;
    }

    public static IntegrationTestHelper Create(TestOption option)
    {
        var helper = option switch
        {
            TestOption.Default => CreateDefault(),
            TestOption.New => CreateNew(),
            _ => throw UnreachableHelper.Unreachable(),
        };
        return helper;
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

    public async Task<DocParseContext> GetContextFromSourceOfTruth(CancellationToken cancellationToken)
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
            @$"{dirName}\zi",
            cancellationToken: cancellationToken);

        string frPath = @$"{dirName}\fr\1.xlsx";
        if (File.Exists(frPath))
        {
            await using var frFile = new FileStream(frPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await FrExcelParser.ParseIntoSchedule(new()
            {
                Context = context,
                InputFile = frFile,
                StringBuilder = new(),
            });
        }

        return context;
    }

    public async Task<Schedule> GetScheduleFromSourceOfTruth(CancellationToken cancellationToken)
    {
        var context = await GetContextFromSourceOfTruth(cancellationToken);
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

    public static async Task<SettingsTask> ScheduleVerify(
        Schedule schedule,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await ScheduleSerializer.Serialize(schedule, stream, hash: "", cancellationToken);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
        var str = reader.ReadToEnd();
        return Verify(new Target("json", str));
    }
}

using MainCli;
using MainCli.BuilderNew.Impl;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Builders;

namespace ScheduleFromDoc.Tests;

public enum TestOption
{
    Default,
    New,
}

public sealed class IntegrationTestHelper : IDisposable
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
    private readonly ServiceProvider _rootServiceProvider;
    public CancellationToken CancellationToken;
    private readonly CancellationTokenSource _cts;
    public IServiceProvider ServiceProvider => _scope.ServiceProvider;
    private readonly IServiceScope _scope;

    public async Task InitializeSchedule()
    {
        await ServiceProvider.InitializeSchedule(CancellationToken);
    }

    public IntegrationTestHelper(
        int year,
        Semester sem)
    {
        _cts = CreateCts();

        Year = year;
        Semester = sem;

        var services = new ServiceCollection();
        services.AddScheduleServices();
        services.AddLogging();
        services.Configure<StudyYearOptions>(opts =>
        {
            opts.StudyYear = year;
            opts.Semester = sem;
        });
        services.Configure<ScheduleBuilderInitializerOptions>(opts =>
        {
            opts.UseCache = false;
            opts.EnrichWithFullNames = false;
        });
        _rootServiceProvider = services.BuildServiceProvider();
        _scope = _rootServiceProvider.CreateScope();
    }

    public static Task<IntegrationTestHelper> Create(TestOption option)
    {
        return option switch
        {
#pragma warning disable CA2000
            TestOption.Default => CreateDefault(),
            TestOption.New => CreateNew(),
#pragma warning restore CA2000
            _ => throw UnreachableHelper.Unreachable(),
        };
    }

    public static async Task<IntegrationTestHelper> CreateDefault()
    {
        var ret = new IntegrationTestHelper(2024, Semester.Sem2);
        try
        {
            await ret.InitializeSchedule();
        }
        catch
        {
            ret.Dispose();
            throw;
        }
        return ret;
    }

    public static async Task<IntegrationTestHelper> CreateNew()
    {
        var ret = new IntegrationTestHelper(2025, Semester.Sem1);
        try
        {
            await ret.InitializeSchedule();
        }
        catch
        {
            ret.Dispose();
            throw;
        }
        return ret;
    }

    public static CancellationTokenSource CreateCts()
    {
        return TestHelper.CreateCts();
    }

    public Schedule GetScheduleFromSourceOfTruth()
    {
        return ServiceProvider.GetRequiredService<Schedule>();
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

    public void Dispose()
    {
        _scope.Dispose();
        _rootServiceProvider.Dispose();
        _cts.Dispose();
    }
}

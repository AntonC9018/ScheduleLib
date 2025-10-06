using System.Diagnostics;
using System.Text;
using MainCli;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleFromDoc.Tests;

public sealed class ScheduleFromDocTests
{
    private static CancellationTokenSource CreateCts()
    {
        var delay = TimeSpan.FromSeconds(10);
        if (Debugger.IsAttached)
        {
            delay = TimeSpan.FromMinutes(10);
        }
        return new CancellationTokenSource(delay);
    }

    private const string VerifyScheduleSnapshotName = "verify_schedule_model";
    private const string ScheduleJsonSnapshotName = "verify_schedule_json";
    private const string ScheduleSnapshotJsonPath = $"{ScheduleJsonSnapshotName}.verified.json";

    [Fact]
    public async Task IntegrationTestWord()
    {
        using var cts = CreateCts();
        var cancellationToken = cts.Token;
        var schedule = await GetScheduleFromWord(cancellationToken);
        var verifyModel = VerifyModelMapper.ToVerifyModel(schedule);
        await Verify(verifyModel)
            .UseFileName(VerifyScheduleSnapshotName);
    }

    [Fact]
    public async Task JsonConversionBackAndForth()
    {
        using var cts = CreateCts();
        // read from json
        // serialize again to another file
        // compare contents
        var cancellationToken = cts.Token;

        const string outputPath = "output.json";
        const string otherOutputPath = "other_output.json";
        {
            var schedule = await GetScheduleFromWord(cancellationToken);
            await using var outputFile = new FileStream(outputPath, FileMode.Create);
            await ScheduleSerializer.Serialize(schedule, outputFile, "", cancellationToken);
            if (Debugger.IsAttached)
            {
                ExplorerHelper.TryOpenExplorerAndSelectFile(outputPath);
            }
        }
        {
            var builder = new ScheduleBuilder();
            builder.SetStudyYear(Year);

            {
                await using var inputFile = File.OpenRead(outputPath);
                var scheduleModel = await ScheduleSerializer.Deserialize(inputFile, cancellationToken);
                ScheduleSerializer.AddToBuilder(builder, scheduleModel);
            }
            {
                var schedule1 = builder.Build();
                await using var outputFile = new FileStream(otherOutputPath, FileMode.Create);
                await ScheduleSerializer.Serialize(schedule1, outputFile, "", cancellationToken);
            }
        }
        {
            var text1 = await File.ReadAllTextAsync(outputPath, cancellationToken);
            var text2 = await File.ReadAllTextAsync(otherOutputPath, cancellationToken);
            Assert.Equal(text1, text2);
        }
    }

    [Fact]
    public async Task JsonSerializationIntegrationTest()
    {
        using var cts = CreateCts();
        var cancellationToken = cts.Token;
        var schedule = await GetScheduleFromWord(cancellationToken);
        using var stream = new MemoryStream();
        await ScheduleSerializer.Serialize(schedule, stream, hash: "", cancellationToken);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
        var str = reader.ReadToEnd();
        await Verify(new Target("json", str))
            .UseFileName(ScheduleJsonSnapshotName);
    }

    [Fact]
    public async Task JsonAndWordModelsAreEquivalent()
    {
        using var cts = CreateCts();
        var cancellationToken = cts.Token;
        await using var reader = File.OpenRead(ScheduleSnapshotJsonPath);
        var scheduleModel = await ScheduleSerializer.Deserialize(reader, cancellationToken);
        var scheduleBuilder = new ScheduleBuilder();
        ScheduleSerializer.AddToBuilder(scheduleBuilder, scheduleModel);
        var jsonSchedule = scheduleBuilder.Build();

        var serializationModel = VerifyModelMapper.ToVerifyModel(jsonSchedule);
        await Verify(serializationModel)
            .UseFileName(VerifyScheduleSnapshotName);
    }

    private const int Year = 2024;

    private static async Task<Schedule> GetScheduleFromWord(CancellationToken cancellationToken)
    {
        var context = DocParseContext.Create(new()
        {
            DayNameProvider = new(),
            CourseNameParserConfig = Config.CourseNameParser,
        });

        context.Schedule.SetStudyYear(Year);

        string dirName = @$"data\{Year}_sem2";
        await Tasks.ParseDocumentDirIntoSchedule(
            context,
            dirName,
            cancellationToken: cancellationToken);

        var schedule = context.Schedule.Build();
        return schedule;
    }
}


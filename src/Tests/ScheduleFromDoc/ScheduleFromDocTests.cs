using System.Diagnostics;
using MainCli;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleFromDoc.Tests;

[CollectionDefinition(IntegrationTestHelper.VerifyScheduleSnapshotName, DisableParallelization = true)]
public class VerifyScheduleTestsCollection : ICollectionFixture<object>;

[Collection(IntegrationTestHelper.VerifyScheduleSnapshotName)]
public sealed class ScheduleFromDocTestExclusive1
{
    [Fact]
    public async Task IntegrationTestWord()
    {
        using var cts = IntegrationTestHelper.CreateCts();
        var cancellationToken = cts.Token;
        var schedule = await IntegrationTestHelper.GetScheduleFromWord(cancellationToken);
        var verifyModel = VerifyModelMapper.ToVerifyModel(schedule);
        await Verify(verifyModel)
            .DisableRequireUniquePrefix()
            .UseFileName(IntegrationTestHelper.VerifyScheduleSnapshotName);
    }

    [Fact]
    public async Task JsonAndWordModelsAreEquivalent()
    {
        using var cts = IntegrationTestHelper.CreateCts();
        var cancellationToken = cts.Token;
        var jsonSchedule = await IntegrationTestHelper.GetScheduleFromJson(
            IntegrationTestHelper.ScheduleSnapshotJsonPath,
            cancellationToken);

        var serializationModel = VerifyModelMapper.ToVerifyModel(jsonSchedule);
        await Verify(serializationModel)
            .DisableRequireUniquePrefix()
            .UseFileName(IntegrationTestHelper.VerifyScheduleSnapshotName);
    }
}

public sealed class ScheduleFromDocTests
{
    [Fact]
    public async Task JsonConversionBackAndForth()
    {
        using var cts = IntegrationTestHelper.CreateCts();
        // read from json
        // serialize again to another file
        // compare contents
        var cancellationToken = cts.Token;

        const string outputPath = "output.json";
        const string otherOutputPath = "other_output.json";
        {
            var schedule = await IntegrationTestHelper.GetScheduleFromWord(cancellationToken);
            await using var outputFile = new FileStream(outputPath, FileMode.Create);
            await ScheduleSerializer.Serialize(schedule, outputFile, "", cancellationToken);
            if (Debugger.IsAttached)
            {
                ExplorerHelper.TryOpenExplorerAndSelectFile(outputPath);
            }
        }
        {
            var builder = new ScheduleBuilder();
            builder.SetStudyYear(IntegrationTestHelper.Year);

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
        using var cts = IntegrationTestHelper.CreateCts();
        var cancellationToken = cts.Token;
        var schedule = await IntegrationTestHelper.GetScheduleFromWord(cancellationToken);
        using var stream = new MemoryStream();
        await ScheduleSerializer.Serialize(schedule, stream, hash: "", cancellationToken);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
        var str = reader.ReadToEnd();
        await Verify(new Target("json", str))
            .UseFileName(IntegrationTestHelper.ScheduleJsonSnapshotName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupCompletelyWorks(bool resetLookup)
    {
        using var cts = IntegrationTestHelper.CreateCts();
        var cancellationToken = cts.Token;
        var context = await IntegrationTestHelper.GetContextFromWord(cancellationToken);
        var schedule = context.Schedule.Build();
        var lookup = context.Schedule.Lookup();

        if (resetLookup)
        {
            context.Schedule.RefreshLookup();
        }

        foreach (var course in schedule.EnumerateCourses())
        {
            foreach (var name in course.Item.Names)
            {
                var id = lookup.Course(name);
                Assert.Equal(course.Id, id);
            }
        }
        foreach (var teacher in schedule.EnumerateTeachers())
        {
            var teachers = lookup.Teachers(teacher.Item.PersonName.LastName);
            Assert.Contains(teacher.Id, teachers);
        }
        foreach (var group in schedule.EnumerateGroups())
        {
            var id = lookup.Group(group.Item.Name);
            Assert.Equal(group.Id, id);
        }
    }
}

public static class IntegrationTestHelper
{
    public const string VerifyScheduleSnapshotName = "verify_schedule_model";
    public const string ScheduleJsonSnapshotName = "verify_schedule_json";
    public const string ScheduleSnapshotJsonPath = $"{ScheduleJsonSnapshotName}.verified.json";

    public const int Year = 2024;

    public static CancellationTokenSource CreateCts()
    {
        return TestHelper.CreateCts();
    }

    public static async Task<DocParseContext> GetContextFromWord(CancellationToken cancellationToken)
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

        return context;
    }

    public static async Task<Schedule> GetScheduleFromWord(CancellationToken cancellationToken)
    {
        var context = await GetContextFromWord(cancellationToken);
        var schedule = context.Schedule.Build();
        return schedule;
    }

    public static async Task<Schedule> GetScheduleFromJson(string jsonPath, CancellationToken cancellationToken)
    {
        await using var reader = File.OpenRead(jsonPath);
        var scheduleModel = await ScheduleSerializer.Deserialize(reader, cancellationToken);
        var scheduleBuilder = new ScheduleBuilder();
        ScheduleSerializer.AddToBuilder(scheduleBuilder, scheduleModel);
        var jsonSchedule = scheduleBuilder.Build();
        return jsonSchedule;
    }
}


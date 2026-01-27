using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Helper;

namespace ScheduleFromDoc.Tests;

using static IntegrationTestHelper;

[CollectionDefinition("common1", DisableParallelization = true)]
public class VerifyScheduleTestsCollection : ICollectionFixture<object>;

[Collection("common1")]
public sealed class ScheduleFromDocTestExclusive1
{
    [Theory]
    [EnumMembersData<TestOption>]
    public async Task IntegrationTestWord(TestOption option)
    {
        using var helper = await Create(option);
        var schedule = helper.GetScheduleFromSourceOfTruth();
        var verify = await ScheduleVerify(schedule, helper.CancellationToken);
        await verify
            .DisableRequireUniquePrefix()
            .UseFileName(VerifyScheduleSnapshotName(option));
    }

    [Theory]
    [EnumMembersData<TestOption>]
    public async Task JsonAndWordModelsAreEquivalent(TestOption option)
    {
        using var cts = CreateCts();
        var cancellationToken = cts.Token;
        var jsonSchedule = await GetScheduleFromJson(
            ScheduleSnapshotJsonPath(option),
            cancellationToken);
        var verify = await ScheduleVerify(jsonSchedule, cancellationToken);
        await verify
            .DisableRequireUniquePrefix()
            .UseFileName(VerifyScheduleSnapshotName(option));
    }
}

public sealed class EnumMembersDataAttribute<T> : MemberDataAttributeBase
    where T : struct, Enum
{
    public static IEnumerable<object[]> Members => Enum.GetValues<T>().Select(x => new object[]{x});

    public EnumMembersDataAttribute() : base(nameof(Members), [])
    {
        MemberType = this.GetType();
    }

    protected override object[]? ConvertDataItem(MethodInfo testMethod, object item)
    {
        if (item == null)
        {
            return null;
        }

        if (item is not object[] array)
        {
            var message = string.Format(
                CultureInfo.CurrentCulture,
                "Property {0} on {1} yielded an item that is not an object[]",
                MemberName,
                MemberType ?? testMethod.DeclaringType);
            throw new ArgumentException(message);
        }

        return array;
    }
}

public sealed class ScheduleFromDocTests
{
    [Theory]
    [EnumMembersData<TestOption>]
    public async Task JsonConversionBackAndForth(TestOption option)
    {
        var optionName = Enum.GetName(option);
        string outputPath = $"{optionName}_output.json";
        string otherOutputPath = $"{optionName}_other_output.json";

        using var helper = await Create(option);
        {
            var schedule = helper.GetScheduleFromSourceOfTruth();
            await using var outputFile = new FileStream(outputPath, FileMode.Create);
            await ScheduleSerializer.Serialize(schedule, outputFile, "", helper.CancellationToken);
            if (Debugger.IsAttached)
            {
                ExplorerHelper.TryOpenExplorerAndSelectFile(outputPath);
            }
        }
        {
            var builder = new ScheduleBuilder();
            builder.SetStudyYear(helper.Year);

            {
                await using var inputFile = File.OpenRead(outputPath);
                var scheduleModel = await ScheduleSerializer.Deserialize(inputFile, helper.CancellationToken);
                ScheduleSerializer.AddToBuilder(builder, scheduleModel);
            }
            {
                var schedule1 = builder.Build();
                await using var outputFile = new FileStream(otherOutputPath, FileMode.Create);
                await ScheduleSerializer.Serialize(schedule1, outputFile, "", helper.CancellationToken);
            }
        }
        {
            var text1 = await File.ReadAllTextAsync(outputPath, helper.CancellationToken);
            var text2 = await File.ReadAllTextAsync(otherOutputPath, helper.CancellationToken);
            Assert.Equal(text1, text2);
        }
    }

    [Theory]
    [EnumMembersData<TestOption>]
    public async Task JsonSerializationIntegrationTest(TestOption option)
    {
        using var helper = await Create(option);
        var schedule = helper.GetScheduleFromSourceOfTruth();
        var settingsTask = await ScheduleVerify(schedule, helper.CancellationToken);
        await settingsTask.UseFileName(ScheduleJsonSnapshotName(option));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupCompletelyWorks(bool resetLookup)
    {
        using var helper = await CreateDefault();
        var schedule = helper.GetScheduleFromSourceOfTruth();
        var lookup = helper.ServiceProvider.GetRequiredService<LookupFacade>();
        var builder = helper.ServiceProvider.GetRequiredService<ScheduleBuilder>();

        if (resetLookup)
        {
            builder.RefreshLookup();
        }

        foreach (var course in schedule.EnumerateCourses())
        {
            foreach (var name in course.Item.Names)
            {
                var id = lookup.Course(name.AsMemory());
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

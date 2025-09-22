using MainCli;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleFromDoc.Tests;

public sealed class ScheduleFromDocTests
{
    [Fact]
    public async Task IntegrationTest()
    {
        using var cts = new CancellationTokenSource(delay: TimeSpan.FromSeconds(10));
        var cancellationToken = cts.Token;

        var context = DocParseContext.Create(new()
        {
            DayNameProvider = new(),
            CourseNameParserConfig = Config.CourseNameParser,
        });

        const int year = 2025;
        context.Schedule.SetStudyYear(year);

        string dirName = @$"data\{year}_sem2";
        await Tasks.ParseDocumentDirIntoSchedule(
            context,
            dirName,
            cancellationToken: cancellationToken);

        var schedule = context.Schedule.Build();
        var verifyModel = VerifyModelMapper.ToVerifyModel(schedule);
        await Verify(verifyModel);
    }
}



using ScheduleLib.Builders;
using Tests.ScheduleCommon;
using WebsiteJsonSchedule;

namespace FmiWebsiteInterop.Tests;

public sealed class FmiWebsiteInteropTests
{
    [Fact]
    public async Task CountTest()
    {
        TeacherBuilderModel.NameModel name = default;
        {
            name.FirstName[0].Full = "Titu";
            name.LastName[0] = "Capcelea";
        }
        var schedule = await ScheduleTestHelper.CreateScheduleForTeacher(name);
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
}

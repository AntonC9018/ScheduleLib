using ScheduleLib;

namespace ScheduleFromDoc.Tests;

public sealed class ScheduleFromDocTests
{
    [Fact]
    public async Task Placeholder()
    {
        var placeholder = new { Message = "Schedule-from-doc tests will be added" };
        await Verify(placeholder);
    }
}



using System.Globalization;
using System.Text.Json;
using ScheduleLib.Cli;

public sealed class CalendarReportingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ReportingBoundaryRendersOnlyHumanOutput(bool json, bool jsonl)
    {
        var previous = Console.Out;
        using var text = new StringWriter();
        var probe = new FormattingProbe();
        try
        {
            Console.SetOut(text);
            using var context = new CliCommandContext("calendar sync",
                new()
                {
                    Json = json,
                    Jsonl = jsonl,
                },
                default);
            context.StreamUpdate(new CalendarReport("desired-0",
                "completed",
                new { index = 0 },
                new ProbeMessage(probe)));
            Assert.Equal(json || jsonl ? 0 : 1, probe.Calls);
            if (jsonl)
            {
                using var document = JsonDocument.Parse(text.ToString());
                Assert.Equal("desired-0", document.RootElement.GetProperty("id").GetString());
                Assert.Equal(0, document.RootElement.GetProperty("item").GetProperty("index").GetInt32());
                Assert.DoesNotContain("human", document.RootElement.EnumerateObject().Select(x => x.Name));
            }
            else
            {
                Assert.Equal(json ? "" : "probe" + Environment.NewLine, text.ToString());
            }
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    [Fact]
    public void PlannedActionValuesRenderToTheExistingJsonStringArray()
    {
        var previous = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            using var context = new CliCommandContext("calendar sync",
                new() { Json = true },
                default);
            context.Finish(new CliCompletion<object?>(0,
                [],
                null)
            {
                ActionMessages =
                [
                    new CalendarDeleteAction("old", 2),
                    new CalendarCreateAction("Lessons"),
                    new CalendarCreateEventsAction(3),
                ],
            });
            using var document = JsonDocument.Parse(text.ToString());
            Assert.Equal(
                new[]
                {
                    "Delete calendar old and its 2 events",
                    "Create calendar Lessons with a new ID",
                    "Create 3 events with new IDs",
                },
                document.RootElement.GetProperty("actions").EnumerateArray().Select(x => x.GetString()));
        }
        finally
        {
            Console.SetOut(previous);
        }
    }

    [Fact]
    public void RenderingUsesTheBoundaryCultureAndPreservesNullFields()
    {
        var start = new DateTimeOffset(2026,
            10,
            18,
            9,
            0,
            0,
            TimeSpan.FromHours(3));
        var message = new CalendarDesiredEventMessage(0,
            null,
            start,
            null,
            []);
        foreach (var cultureName in new[]
                 {
                     "en-US",
                     "ro-RO",
                 })
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal($"Create event [0]: ; {start.ToString(culture)}–; ", message.Format(culture));
        }
        Assert.Equal("create event: not-attempted; desired index 2",
            new CalendarNotAttemptedMessage("create event", 2).Format(CultureInfo.InvariantCulture));
    }

    private sealed class FormattingProbe
    {
        public int Calls;
    }

    private readonly record struct ProbeMessage(FormattingProbe Probe) : ICliMessage
    {
        public string Format(IFormatProvider provider)
        {
            Probe.Calls++;
            return "probe";
        }
    }
}

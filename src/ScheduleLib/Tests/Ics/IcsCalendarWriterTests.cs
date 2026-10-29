using System.Text;
using ScheduleLib.Generation.Ics;

namespace Tests.Ics;

public sealed class IcsCalendarWriterTests
{
    private static IcsCalendar Calendar(params IcsEvent[] events)
    {
        return new()
        {
            Name = "Orar IA2401 Sem 1",
            Events = events,
        };
    }

    private static IcsEvent Event(string? summary = null)
    {
        return new()
        {
            Summary = summary ?? "Programare orientată obiect (Curs)",
            Description = "Curmanschii A.  B407",
            Location = "B407",
            Start = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 8,
                minute: 0,
                second: 0),
            End = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 9,
                minute: 30,
                second: 0),
        };
    }

    private static string[] PhysicalLines(IcsCalendar calendar)
    {
        var bytes = IcsCalendarWriter.Write(calendar);
        var text = Encoding.UTF8.GetString(bytes);
        return text.Split("\r\n");
    }

    private static string WriteText(IcsCalendar calendar)
    {
        var encoding = Encoding.UTF8;
        byte[] bytes = IcsCalendarWriter.Write(calendar);
        string stringValue = encoding.GetString(bytes);
        return stringValue;
    }

    [Fact]
    public void CalendarHeader_HasRequiredProperties()
    {
        var lines = PhysicalLines(Calendar());

        Assert.Contains("BEGIN:VCALENDAR", lines);
        Assert.Contains("VERSION:2.0", lines);
        Assert.Contains("PRODID:-//ScheduleLib//Schedule ICS 1.0//EN", lines);
        Assert.Contains("CALSCALE:GREGORIAN", lines);
        Assert.Contains("METHOD:PUBLISH", lines);
        Assert.Contains("X-WR-CALNAME:Orar IA2401 Sem 1", lines);
        Assert.Contains("X-WR-TIMEZONE:Europe/Chisinau", lines);
        Assert.Contains("END:VCALENDAR", lines);
    }

    [Fact]
    public void Calendar_HasChisinauVTimeZone()
    {
        var lines = PhysicalLines(Calendar());

        Assert.Contains("BEGIN:VTIMEZONE", lines);
        Assert.Contains("TZID:Europe/Chisinau", lines);
        Assert.Contains("TZOFFSETFROM:+0300", lines);
        Assert.Contains("TZOFFSETTO:+0200", lines);
        Assert.Contains("END:VTIMEZONE", lines);
    }

    [Fact]
    public void Lines_UseCrLfAndFileEndsWithCrLf()
    {
        var text = WriteText(Calendar());

        string expectedSubstring2 = "\n";
        string actualString2 = text.Replace("\r\n", "");
        Assert.DoesNotContain(expectedSubstring: expectedSubstring2, actualString: actualString2);
        Assert.EndsWith(expectedEndString: "\r\n", actualString: text);
    }

    [Fact]
    public void Event_HasRequiredPropertiesWithTimezone()
    {
        var calendar = Calendar(Event());
        var lines = PhysicalLines(calendar);

        Assert.Contains("BEGIN:VEVENT", lines);
        string expectedRegexPattern2 = @"DTSTAMP:\d{8}T\d{6}Z";
        string actualString2 = SingleLine(lines, "DTSTAMP");
        Assert.Matches(expectedRegexPattern: expectedRegexPattern2, actualString: actualString2);
        Assert.Contains("DTSTART;TZID=Europe/Chisinau:20260907T080000", lines);
        Assert.Contains("DTEND;TZID=Europe/Chisinau:20260907T093000", lines);
        Assert.Contains("SUMMARY:Programare orientată obiect (Curs)", lines);
        Assert.Contains("DESCRIPTION:Curmanschii A.  B407", lines);
        Assert.Contains("LOCATION:B407", lines);
        Assert.Contains("END:VEVENT", lines);
    }

    [Fact]
    public void Event_HasNoValarm()
    {
        var calendar = Calendar(Event());
        var lines = PhysicalLines(calendar);

        Assert.DoesNotContain(lines, l => l.StartsWith("BEGIN:VALARM"));
    }

    [Fact]
    public void Uid_IsStableForSameContent()
    {
        var calendar = Calendar(Event());
        var first = WriteText(calendar);
        var calendar2 = Calendar(Event());
        var second = WriteText(calendar2);

        string expected2 = UidOf(first);
        string actual2 = UidOf(second);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void Uid_DiffersForDifferentContent()
    {
        IcsEvent CalculateEvents()
        {
            var events = Event("Curs A");
            return events;
        }

        IcsCalendar CalculateCalendar()
        {
            var calendar = Calendar(CalculateEvents());
            return calendar;
        }

        var a = WriteText(CalculateCalendar());

        IcsEvent CalculateEvents2()
        {
            var events2 = Event("Curs B");
            return events2;
        }

        IcsCalendar CalculateCalendar2()
        {
            var calendar2 = Calendar(CalculateEvents2());
            return calendar2;
        }

        var b = WriteText(CalculateCalendar2());

        string expected2 = UidOf(a);
        string actual2 = UidOf(b);
        Assert.NotEqual(expected: expected2, actual: actual2);
    }

    [Fact]
    public void Uid_UsesExplicitOverrideWhenGiven()
    {
        var calendar = Calendar(new IcsEvent()
        {
            Summary = "x",
            Start = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 8,
                minute: 0,
                second: 0),
            End = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 9,
                minute: 30,
                second: 0),
            Uid = "explicit@schedulelib",
        });

        string expected = "UID:explicit@schedulelib";
        string[] collection = PhysicalLines(calendar);
        IEnumerable<string> collection2 = collection;
        Assert.Contains(expected, collection2);
    }

    [Fact]
    public void Text_EscapesSpecialCharacters()
    {
        var lines = PhysicalLines(Calendar(new IcsEvent()
        {
            Summary = @"Back\slash, comma; semi",
            Start = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 8,
                minute: 0,
                second: 0),
            End = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 9,
                minute: 0,
                second: 0),
        }));

        Assert.Contains(@"SUMMARY:Back\\slash\, comma\; semi", lines);
    }

    [Fact]
    public void Text_EscapesNewlines()
    {
        var lines = PhysicalLines(Calendar(new IcsEvent()
        {
            Summary = "line1\r\nline2\nline3",
            Start = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 8,
                minute: 0,
                second: 0),
            End = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 9,
                minute: 0,
                second: 0),
        }));

        Assert.Contains(@"SUMMARY:line1\nline2\nline3", lines);
    }

    [Fact]
    public void FoldedLines_StayWithin75OctetsAndUnfold()
    {
        // Cyrillic chars are 2 octets each, an emoji is 4: both stress the
        // octet-vs-char distinction and the 2-octet/4-octet UTF-8 ranges.
        var longText = $"{new string('а', 60)}{new string('б', 30)}🏝️{new string('в', 40)}";
        var calendar = Calendar(new IcsEvent()
        {
            Summary = longText,
            Start = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 8,
                minute: 0,
                second: 0),
            End = new DateTime(
                year: 2026,
                month: 9,
                day: 7,
                hour: 9,
                minute: 0,
                second: 0),
        });

        var lines = PhysicalLines(calendar);
        var summaryLines = lines
            .SkipWhile(l => !l.StartsWith("SUMMARY:"))
            .TakeWhile((l, i) =>
            {
                bool CheckCondition()
                {
                    if (i == 0)
                    {
                        return true;
                    }

                    if (l.StartsWith(" "))
                    {
                        return true;
                    }

                    if (l.StartsWith("\t"))
                    {
                        return true;
                    }

                    return false;
                }

                return CheckCondition();
            })
            .ToArray();

        Assert.True(summaryLines.Length > 1, "expected the summary to be folded");
        foreach (var line in summaryLines)
        {
            int byteCount = Encoding.UTF8.GetByteCount(line);
            bool condition = byteCount <= 75;
            string userMessage = $"'{line}' exceeds 75 octets";
            Assert.True(condition, userMessage);
        }

        var unfolded = string.Join(
            "",
            summaryLines
                .Select((l, i) => i == 0 ? l : l[1..]));
        string text = $"SUMMARY:{longText}";
        string expected2 = text;
        string actual2 = unfolded;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void Fold_KeepsSurrogatePairsInOneSegment()
    {
        // 74 ASCII chars then an emoji: the pair does not fit in the first
        // segment, so it must move to the continuation line whole.
        string text = new string('a', 67);
        string text2 = $"SUMMARY:{text}🏝️";
        var line = text2;

        var folded = IcsCalendarWriter.Fold(line);
        var segments = folded.Split("\r\n");

        Assert.Equal(expected: 2, actual: segments.Length);
        int expected2 = 75;
        int actual2 = Encoding.UTF8.GetByteCount(segments[0]);
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: " 🏝️", actual: segments[1]);
    }

    private static string UidOf(string calendarText)
    {
        return calendarText
            .Split("\r\n")
            .Single(l => l.StartsWith("UID:"));
    }

    private static string SingleLine(string[] lines, string prefix)
    {
        return lines.Single(l => l.StartsWith(prefix));
    }
}

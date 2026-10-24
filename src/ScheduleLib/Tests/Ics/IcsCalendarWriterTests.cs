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
            Start = new DateTime(year: 2026, month: 9, day: 7, hour: 8, minute: 0, second: 0),
            End = new DateTime(year: 2026, month: 9, day: 7, hour: 9, minute: 30, second: 0),
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
        return Encoding.UTF8.GetString(IcsCalendarWriter.Write(calendar));
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

        Assert.DoesNotContain(expectedSubstring: "\n", actualString: text.Replace("\r\n", ""));
        Assert.EndsWith(expectedEndString: "\r\n", actualString: text);
    }

    [Fact]
    public void Event_HasRequiredPropertiesWithTimezone()
    {
        var lines = PhysicalLines(Calendar(Event()));

        Assert.Contains("BEGIN:VEVENT", lines);
        Assert.Matches(expectedRegexPattern: @"DTSTAMP:\d{8}T\d{6}Z", actualString: SingleLine(lines, "DTSTAMP"));
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
        var lines = PhysicalLines(Calendar(Event()));

        Assert.DoesNotContain(lines, l => l.StartsWith("BEGIN:VALARM"));
    }

    [Fact]
    public void Uid_IsStableForSameContent()
    {
        var first = WriteText(Calendar(Event()));
        var second = WriteText(Calendar(Event()));

        Assert.Equal(expected: UidOf(first), actual: UidOf(second));
    }

    [Fact]
    public void Uid_DiffersForDifferentContent()
    {
        var a = WriteText(Calendar(Event("Curs A")));
        var b = WriteText(Calendar(Event("Curs B")));

        Assert.NotEqual(expected: UidOf(a), actual: UidOf(b));
    }

    [Fact]
    public void Uid_UsesExplicitOverrideWhenGiven()
    {
        var calendar = Calendar(new IcsEvent()
        {
            Summary = "x",
            Start = new DateTime(year: 2026, month: 9, day: 7, hour: 8, minute: 0, second: 0),
            End = new DateTime(year: 2026, month: 9, day: 7, hour: 9, minute: 30, second: 0),
            Uid = "explicit@schedulelib",
        });

        Assert.Contains("UID:explicit@schedulelib", PhysicalLines(calendar));
    }

    [Fact]
    public void Text_EscapesSpecialCharacters()
    {
        var lines = PhysicalLines(Calendar(new IcsEvent()
        {
            Summary = @"Back\slash, comma; semi",
            Start = new DateTime(year: 2026, month: 9, day: 7, hour: 8, minute: 0, second: 0),
            End = new DateTime(year: 2026, month: 9, day: 7, hour: 9, minute: 0, second: 0),
        }));

        Assert.Contains(@"SUMMARY:Back\\slash\, comma\; semi", lines);
    }

    [Fact]
    public void Text_EscapesNewlines()
    {
        var lines = PhysicalLines(Calendar(new IcsEvent()
        {
            Summary = "line1\r\nline2\nline3",
            Start = new DateTime(year: 2026, month: 9, day: 7, hour: 8, minute: 0, second: 0),
            End = new DateTime(year: 2026, month: 9, day: 7, hour: 9, minute: 0, second: 0),
        }));

        Assert.Contains(@"SUMMARY:line1\nline2\nline3", lines);
    }

    [Fact]
    public void FoldedLines_StayWithin75OctetsAndUnfold()
    {
        // Cyrillic chars are 2 octets each, an emoji is 4: both stress the
        // octet-vs-char distinction and the 2-octet/4-octet UTF-8 ranges.
        var longText = (string) $"{(new string('а', 60))}{(new string('б', 30))}🏝️{(new string('в', 40))}";
        var calendar = Calendar(new IcsEvent()
        {
            Summary = longText,
            Start = new DateTime(year: 2026, month: 9, day: 7, hour: 8, minute: 0, second: 0),
            End = new DateTime(year: 2026, month: 9, day: 7, hour: 9, minute: 0, second: 0),
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
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"'{line}' exceeds 75 octets");
        }

        var unfolded = string.Join("", summaryLines
            .Select((l, i) => i == 0 ? l : l[1..]));
        Assert.Equal(expected: (string) $"SUMMARY:{longText}", actual: unfolded);
    }

    [Fact]
    public void Fold_KeepsSurrogatePairsInOneSegment()
    {
        // 74 ASCII chars then an emoji: the pair does not fit in the first
        // segment, so it must move to the continuation line whole.
        var line = (string) $"SUMMARY:{(new string('a', 67))}🏝️";

        var folded = IcsCalendarWriter.Fold(line);
        var segments = folded.Split("\r\n");

        Assert.Equal(expected: 2, actual: segments.Length);
        Assert.Equal(expected: 75, actual: Encoding.UTF8.GetByteCount(segments[0]));
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

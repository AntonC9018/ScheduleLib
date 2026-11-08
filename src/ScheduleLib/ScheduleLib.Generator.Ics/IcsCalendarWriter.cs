using System.Security.Cryptography;
using System.Text;

namespace ScheduleLib.Generation.Ics;

/// <summary>
/// Writes an <see cref="IcsCalendar"/> as an RFC 5545 iCalendar file.
/// </summary>
/// <remarks>
/// Compliance choices:
/// CRLF line endings, lines folded at 75 octets, TEXT escaping,
/// METHOD:PUBLISH, CALSCALE:GREGORIAN and the X-WR-CALNAME / X-WR-TIMEZONE
/// hints Apple and Google honor. The VTIMEZONE block is a fixed Europe/Chisinau
/// definition (EET/EEST, last Sundays of March/October): TimeZoneInfo cannot
/// produce the historical rules most importers expect, and the schedule
/// timezone is fixed anyway.
///
/// Event UIDs default to a SHA-256 hash of the event content plus the calendar
/// name, so regenerating the same schedule yields the same UIDs; that is the
/// correct update signal for subscription feeds and the best a one-time
/// import can do. No VALARMs are emitted: reminder preference is personal and
/// per-event alarms would override the user's calendar defaults.
/// </remarks>
public static class IcsCalendarWriter
{
    private const string ProdId = "-//ScheduleLib//Schedule ICS 1.0//EN";

    public static byte[] Write(IcsCalendar calendar)
    {
        using var stream = new MemoryStream();
        Write(stream, calendar);
        return stream.ToArray();
    }

    public static void Write(Stream output, IcsCalendar calendar)
    {
        // No BOM: some importers treat one as part of the first property name.

        using var writer = new StreamWriter(
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true);
        writer.NewLine = "\r\n";

        writer.WriteLine("BEGIN:VCALENDAR");
        writer.WriteLine("VERSION:2.0");
        writer.WriteLine($"PRODID:{ProdId}");
        writer.WriteLine("CALSCALE:GREGORIAN");
        writer.WriteLine("METHOD:PUBLISH");
        WriteEscapedLine(
            writer,
            name: "X-WR-CALNAME",
            value: calendar.Name);
        writer.WriteLine($"X-WR-TIMEZONE:{calendar.TimeZoneId}");

        WriteVTimeZone(writer, calendar.TimeZoneId);

        var dtStamp = DateTime.UtcNow;
        foreach (var e in calendar.Events)
        {
            WriteEvent(
                writer,
                calendar,
                e,
                dtStamp);
        }

        writer.WriteLine("END:VCALENDAR");
        writer.Flush();
    }

    private static void WriteEvent(
        StreamWriter writer,
        IcsCalendar calendar,
        IcsEvent e,
        DateTime dtStamp)
    {
        writer.WriteLine("BEGIN:VEVENT");
        WriteEscapedLine(
            writer,
            name: "UID",
            value: e.Uid ?? DefaultUid(calendar, e));
        string utcStamp = FormatUtc(dtStamp);
        writer.WriteLine($"DTSTAMP:{utcStamp}");
        writer.WriteLine($"DTSTART;TZID={calendar.TimeZoneId}:{FormatLocal(e.Start)}");
        writer.WriteLine($"DTEND;TZID={calendar.TimeZoneId}:{FormatLocal(e.End)}");
        WriteEscapedLine(
            writer,
            name: "SUMMARY",
            value: e.Summary);
        if (e.Description is { } description)
        {
            WriteEscapedLine(
                writer,
                name: "DESCRIPTION",
                value: description);
        }
        if (e.Location is { } location)
        {
            WriteEscapedLine(
                writer,
                name: "LOCATION",
                value: location);
        }
        writer.WriteLine("END:VEVENT");
    }

    private static string DefaultUid(IcsCalendar calendar, IcsEvent e)
    {
        string startText = FormatLocal(e.Start);
        string endText = FormatLocal(e.End);
        string content = string.Join(
            '|',
            calendar.Name,
            e.Summary,
            e.Description,
            e.Location,
            startText,
            endText);
        byte[] source = Encoding.UTF8.GetBytes(content);
        byte[] hash = SHA256.HashData(source);
        string hashText = Convert.ToHexString(hash);
        return $"{hashText}@schedulelib";
    }

    private static void WriteEscapedLine(
        StreamWriter writer,
        string name,
        string value)
    {
        writer.WriteLine(Fold($"{name}:{EscapeText(value)}"));
    }

    private static string EscapeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case ';':
                    sb.Append("\\;");
                    break;
                case ',':
                    sb.Append("\\,");
                    break;
                case '\r':
                    // \r\n collapses to one escaped newline; a lone \r is dropped.
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string FormatLocal(DateTime time)
    {
        return time.ToString("yyyyMMdd'T'HHmmss");
    }

    private static string FormatUtc(DateTime time)
    {
        return time.ToString("yyyyMMdd'T'HHmmss'Z'");
    }

    /// <summary>
    /// Folds a content line to lines of at most 75 octets: continuations begin
    /// with a single space (counted against the limit) and the line is cut
    /// between characters, never inside a UTF-8 sequence or a surrogate pair.
    /// </summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75)
        {
            return line;
        }

        static bool IsSurrogatePair(string text, int index)
        {
            if (!char.IsHighSurrogate(text[index]))
            {
                return false;
            }
            int nextIndex = index + 1;
            if (nextIndex >= text.Length)
            {
                return false;
            }
            if (char.IsLowSurrogate(text[nextIndex]))
            {
                return true;
            }
            return false;
        }

        static int Octets(char c)
        {
            // UTF-16 BMP characters encode to 1 octet up to U+007F, 2 up to
            // U+07FF and 3 beyond; surrogate pairs are handled by the caller.
            if (c <= '\x7F')
            {
                return 1;
            }
            if (c <= '\x7FF')
            {
                return 2;
            }
            return 3;
        }

        int estimatedFoldCount = line.Length / 75;
        int foldingOverhead = estimatedFoldCount * 2;
        int foldedLength = line.Length + foldingOverhead;
        int capacity = foldedLength + 2;
        var sb = new StringBuilder(capacity);
        int segmentStart = 0;
        int segmentOctets = 0;
        int limit = 75;
        for (int i = 0; i < line.Length;)
        {
            int charLen = 1;
            int charOctets;

            if (IsSurrogatePair(line, i))
            {
                charLen = 2;
                charOctets = 4;
            }
            else
            {
                charOctets = Octets(line[i]);
            }
            if (segmentOctets + charOctets > limit)
            {
                sb.Append(
                    line,
                    startIndex: segmentStart,
                    count: i - segmentStart);
                sb.Append("\r\n ");
                segmentStart = i;
                segmentOctets = 0;
                limit = 74;
                continue;
            }
            segmentOctets += charOctets;
            i += charLen;
        }
        sb.Append(
            line,
            startIndex: segmentStart,
            count: line.Length - segmentStart);
        return sb.ToString();
    }

    private static void WriteVTimeZone(StreamWriter writer, string timeZoneId)
    {
        if (timeZoneId != "Europe/Chisinau")
        {
            throw new MissingVTimeZoneDefinitionException(timeZoneId);
        }

        writer.WriteLine("BEGIN:VTIMEZONE");
        writer.WriteLine($"TZID:{timeZoneId}");
        writer.WriteLine("BEGIN:STANDARD");
        writer.WriteLine("DTSTART:19701025T040000");
        writer.WriteLine("RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU");
        writer.WriteLine("TZOFFSETFROM:+0300");
        writer.WriteLine("TZOFFSETTO:+0200");
        writer.WriteLine("TZNAME:EET");
        writer.WriteLine("END:STANDARD");
        writer.WriteLine("BEGIN:DAYLIGHT");
        writer.WriteLine("DTSTART:19700329T030000");
        writer.WriteLine("RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU");
        writer.WriteLine("TZOFFSETFROM:+0200");
        writer.WriteLine("TZOFFSETTO:+0300");
        writer.WriteLine("TZNAME:EEST");
        writer.WriteLine("END:DAYLIGHT");
        writer.WriteLine("END:VTIMEZONE");
    }
}

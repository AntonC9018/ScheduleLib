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
        WriteEscapedLine(writer, "X-WR-CALNAME", calendar.Name);
        writer.WriteLine($"X-WR-TIMEZONE:{calendar.TimeZoneId}");

        WriteVTimeZone(writer, calendar.TimeZoneId);

        var dtStamp = DateTime.UtcNow;
        foreach (var e in calendar.Events)
        {
            WriteEvent(writer, calendar, e, dtStamp);
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
        WriteEscapedLine(writer, "UID", e.Uid ?? DefaultUid(calendar, e));
        writer.WriteLine($"DTSTAMP:{FormatUtc(dtStamp)}");
        writer.WriteLine($"DTSTART;TZID={calendar.TimeZoneId}:{FormatLocal(e.Start)}");
        writer.WriteLine($"DTEND;TZID={calendar.TimeZoneId}:{FormatLocal(e.End)}");
        WriteEscapedLine(writer, "SUMMARY", e.Summary);
        if (e.Description is { } description)
        {
            WriteEscapedLine(writer, "DESCRIPTION", description);
        }
        if (e.Location is { } location)
        {
            WriteEscapedLine(writer, "LOCATION", location);
        }
        writer.WriteLine("END:VEVENT");
    }

    private static string DefaultUid(IcsCalendar calendar, IcsEvent e)
    {
        string content = string.Join('|',
            calendar.Name,
            e.Summary,
            e.Description,
            e.Location,
            FormatLocal(e.Start),
            FormatLocal(e.End));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash) + "@schedulelib";
    }

    private static void WriteEscapedLine(StreamWriter writer, string name, string value)
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

        var sb = new StringBuilder(line.Length + line.Length / 75 * 2 + 2);
        int segmentStart = 0;
        int segmentOctets = 0;
        int limit = 75;
        for (int i = 0; i < line.Length;)
        {
            int charLen = 1;
            int charOctets;
            if (char.IsHighSurrogate(line[i])
                && i + 1 < line.Length
                && char.IsLowSurrogate(line[i + 1]))
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
                sb.Append(line, segmentStart, i - segmentStart);
                sb.Append("\r\n ");
                segmentStart = i;
                segmentOctets = 0;
                limit = 74;
                continue;
            }
            segmentOctets += charOctets;
            i += charLen;
        }
        sb.Append(line, segmentStart, line.Length - segmentStart);
        return sb.ToString();

        static int Octets(char c)
        {
            // UTF-16 BMP characters encode to 1 octet up to U+007F, 2 up to
            // U+07FF and 3 beyond; surrogate pairs are handled by the caller.
            return c <= '\x7F' ? 1
                : c <= '\x7FF' ? 2
                : 3;
        }
    }

    private static void WriteVTimeZone(StreamWriter writer, string timeZoneId)
    {
        if (timeZoneId != "Europe/Chisinau")
        {
            throw new NotSupportedException(
                $"No VTIMEZONE definition for '{timeZoneId}'.");
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

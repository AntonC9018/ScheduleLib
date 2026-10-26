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
        UTF8Encoding CalculateEncoding()
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return encoding;
        }

        using var writer = new StreamWriter(
            output,
            CalculateEncoding(),
            leaveOpen: true);
        writer.NewLine = "\r\n";

        writer.WriteLine("BEGIN:VCALENDAR");
        writer.WriteLine("VERSION:2.0");
        var streamWriter = writer;
        string value2 = $"PRODID:{ProdId}";
        streamWriter.WriteLine(value2);
        writer.WriteLine("CALSCALE:GREGORIAN");
        writer.WriteLine("METHOD:PUBLISH");
        WriteEscapedLine(writer,
            name: "X-WR-CALNAME",
            value: calendar.Name);
        var streamWriter2 = writer;
        string value3 = $"X-WR-TIMEZONE:{calendar.TimeZoneId}";
        streamWriter2.WriteLine(value3);

        WriteVTimeZone(writer, calendar.TimeZoneId);

        var dtStamp = DateTime.UtcNow;
        foreach (var e in calendar.Events)
        {
            WriteEvent(writer,
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
        WriteEscapedLine(writer,
            name: "UID",
            value: e.Uid ?? DefaultUid(calendar, e));
        var streamWriter = writer;
        string formatUtc = FormatUtc(dtStamp);
        string value2 = $"DTSTAMP:{formatUtc}";
        streamWriter.WriteLine(value2);
        writer.WriteLine($"DTSTART;TZID={calendar.TimeZoneId}:{FormatLocal(e.Start)}");
        writer.WriteLine($"DTEND;TZID={calendar.TimeZoneId}:{FormatLocal(e.End)}");
        WriteEscapedLine(writer,
            name: "SUMMARY",
            value: e.Summary);
        if (e.Description is { } description)
        {
            WriteEscapedLine(writer,
                name: "DESCRIPTION",
                value: description);
        }
        if (e.Location is { } location)
        {
            WriteEscapedLine(writer,
                name: "LOCATION",
                value: location);
        }
        writer.WriteLine("END:VEVENT");
    }

    private static string DefaultUid(IcsCalendar calendar, IcsEvent e)
    {
        string CalculateFormatLocal()
        {
            string formatLocal = FormatLocal(e.Start);
            return formatLocal;
        }

        string CalculateFormatLocal2()
        {
            string formatLocal2 = FormatLocal(e.End);
            return formatLocal2;
        }

        string content = string.Join('|',
            calendar.Name,
            e.Summary,
            e.Description,
            e.Location,
            CalculateFormatLocal(),
            CalculateFormatLocal2());
        byte[] source = Encoding.UTF8.GetBytes(content);
        byte[] hash = SHA256.HashData(source);
        string toHexString = Convert.ToHexString(hash);
        string text = $"{toHexString}@schedulelib";
        string text2 = text;
        return text2;
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

        int int32 = line.Length;
        int int322 = line.Length / 75;
        int int323 = int322 * 2;
        int int324 = int32 + int323;
        int capacity = int324 + 2;
        var sb = new StringBuilder(capacity);
        int segmentStart = 0;
        int segmentOctets = 0;
        int limit = 75;
        for (int i = 0; i < line.Length;)
        {
            int charLen = 1;
            int charOctets;

            bool CheckCondition()
            {
                if (!char.IsHighSurrogate(line[i]))
                {
                    return false;
                }

                if (!(i + 1 < line.Length))
                {
                    return false;
                }

                if (char.IsLowSurrogate(line[i + 1]))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
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
                sb.Append(line,
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
        sb.Append(line,
            startIndex: segmentStart,
            count: line.Length - segmentStart);
        return sb.ToString();

        static int Octets(char c)
        {
            // UTF-16 BMP characters encode to 1 octet up to U+007F, 2 up to
            // U+07FF and 3 beyond; surrogate pairs are handled by the caller.
            int int32;
            if (c <= '\x7F')
            {
                int32 = 1;
            }
            else
            {
                int int322;
                if (c <= '\x7FF')
                {
                    int322 = 2;
                }
                else
                {
                    int322 = 3;
                }

                int32 = int322;
            }

            return int32;
        }
    }

    private static void WriteVTimeZone(StreamWriter writer, string timeZoneId)
    {
        if (timeZoneId != "Europe/Chisinau")
        {
            throw new MissingVTimeZoneDefinitionException(timeZoneId);
        }

        writer.WriteLine("BEGIN:VTIMEZONE");
        var streamWriter = writer;
        string value = $"TZID:{timeZoneId}";
        streamWriter.WriteLine(value);
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

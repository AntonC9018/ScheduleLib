using System.Text;
using AutoConstructor.Attributes;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Generation;

namespace ScheduleLib.Application.Core;

/// <summary>
/// Exports one iCalendar (.ics) file per teacher, importable into Google Calendar
/// (and any other RFC 5545 client) as a file.
/// The events match what <see cref="UpdateLessonsInGoogleCalendarTaskHandler"/> would upload.
/// </summary>
[AutoConstructor]
public sealed partial class GenerateIcsCalendarsTaskHandler
{
    private readonly ScheduledTimeEventsProvider _eventsProvider;
    private readonly IOptions<StudyYearOptions> _studyYearOptions;
    private readonly LessonTextDisplayHandler.Services _lessonDisplayServices;
    private readonly LessonTimeConfig _timeConfig;
    private readonly Schedule _schedule;

    public readonly struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public required OutputDirectory OutputDirectory { get; init; }
    }

    public async Task Run(RunParams p)
    {
        var lessonDisplay = new LessonTextDisplayHandler(_lessonDisplayServices, new()
        {
            PrintsTeacherName = false,
            PrintsGroupNames = true,
            PrintsSubGroup = true,
        });

        var studyYear = _studyYearOptions.Value;
        var timestamp = DateTime.UtcNow;

        var baseFilter = FilterHelper.Builder()
            .WithLatestPeriod(_schedule);

        var fileNameBuilder = new StringBuilder();
        int writtenCount = 0;
        for (int teacherId = 0; teacherId < _schedule.Teachers.Length; teacherId++)
        {
            p.CancellationToken.ThrowIfCancellationRequested();

            var filter = baseFilter.WithTeacher(new(teacherId));
            var filteredSchedule = _schedule.Filter(filter);
            var timeEvents = _eventsProvider
                .Get(new()
                {
                    Semester = studyYear.Semester,
                    Lessons = filteredSchedule.Lessons,
                })
                .OrderBy(x => x.Event.First)
                .ToArray();

            if (timeEvents.Length == 0)
            {
                continue;
            }

            var teacher = _schedule.Teachers[teacherId];
            TeacherNameHelper.AsFileName(fileNameBuilder, teacher.PersonName);
            fileNameBuilder.Append(".ics");
            var fileName = fileNameBuilder.ToStringAndClear();

            var calendar = new IcsCalendar(teacher.PersonName.ToString(), timestamp);
            for (int eventIndex = 0; eventIndex < timeEvents.Length; eventIndex++)
            {
                calendar.AddEvent(CreateEvent(
                    lessonDisplay,
                    teacherId,
                    studyYear,
                    timeEvents[eventIndex],
                    eventIndex));
            }

            await using var outputFile = p.OutputDirectory.OpenFile(fileName, FileMode.Create, FileAccess.Write);
            await calendar.WriteTo(outputFile, p.CancellationToken);
            writtenCount++;
        }

        Console.WriteLine($"Wrote {writtenCount} calendar files.");
    }

    private IcsEvent CreateEvent(
        LessonTextDisplayHandler lessonDisplay,
        int teacherId,
        StudyYearOptions studyYear,
        TimeEvent timeEvent,
        int eventIndex)
    {
        var lesson = _schedule.Get(timeEvent.LessonId);
        var course = _schedule.Get(lesson.Lesson.Course);

        var notRichText = new NotRichText();
        lessonDisplay.Handle(new()
        {
            Lesson = lesson,
            StringBuilder = new(),
            ColumnWidth = 1,
            LessonTimeConfig = _timeConfig,
            Schedule = _schedule,
            TextDescriptor = notRichText,
        });

        string? GetRecurrence()
        {
            var e = timeEvent.Event;
            if (!e.IsRepeated)
            {
                return null;
            }
            return $"FREQ=DAILY;INTERVAL={e.DayInterval};COUNT={e.Count}";
        }

        var lessonId = timeEvent.LessonId;
        return new()
        {
            Uid = $"schedulelib-{studyYear.StudyYear}-{studyYear.Semester.AsOrdinal()}"
                + $"-t{teacherId}-{lessonId.Regularity}{lessonId.Id}-e{eventIndex}@schedulelib",
            Summary = course.FullName,
            Description = notRichText.GetString().TrimEnd('\r', '\n'),
            Location = IcsConstants.Location,
            Start = IcsConstants.FormatDateTime(timeEvent.Event.First, timeEvent.TimeInterval.Start),
            End = IcsConstants.FormatDateTime(timeEvent.Event.First, timeEvent.TimeInterval.End),
            RecurrenceRule = GetRecurrence(),
        };
    }
}

file static class IcsConstants
{
    public const string TimeZoneId = "Europe/Chisinau";
    public const string Location = "Chișinău, Moldova";
    public const string ProdId = "-//ScheduleLib//ScheduleLib calendar export 1.0//EN";

    public static string FormatDateTime(DateOnly date, TimeOnly time)
    {
        return $"{date:yyyyMMdd}T{time:HHmmss}";
    }

    public static string FormatTimestamp(DateTime utcTimestamp)
    {
        return $"{utcTimestamp:yyyyMMdd'T'HHmmss'Z'}";
    }

    // RFC 5545 3.3.11.
    public static string EscapeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                {
                    sb.Append(@"\\");
                    break;
                }
                case ';':
                {
                    sb.Append(@"\;");
                    break;
                }
                case ',':
                {
                    sb.Append(@"\,");
                    break;
                }
                case '\n':
                {
                    sb.Append(@"\n");
                    break;
                }
                case '\r':
                {
                    // Folded together with the following '\n'.
                    break;
                }
                default:
                {
                    sb.Append(c);
                    break;
                }
            }
        }
        return sb.ToString();
    }
}

internal sealed class IcsEvent
{
    public required string Uid { get; init; }
    public required string Summary { get; init; }
    public required string Description { get; init; }
    public required string Location { get; init; }
    public required string Start { get; init; }
    public required string End { get; init; }
    public string? RecurrenceRule { get; init; }
}

file sealed class IcsCalendar
{
    private readonly string _timestamp;
    private readonly List<string> _lines = [];

    public IcsCalendar(string calendarName, DateTime timestampUtc)
    {
        _timestamp = IcsConstants.FormatTimestamp(timestampUtc);
        _lines.Add("BEGIN:VCALENDAR");
        _lines.Add("VERSION:2.0");
        _lines.Add($"PRODID:{IcsConstants.ProdId}");
        _lines.Add("CALSCALE:GREGORIAN");
        _lines.Add("METHOD:PUBLISH");
        _lines.Add($"X-WR-CALNAME:{IcsConstants.EscapeText(calendarName)}");
        _lines.Add($"X-WR-TIMEZONE:{IcsConstants.TimeZoneId}");
    }

    public void AddEvent(IcsEvent e)
    {
        _lines.Add("BEGIN:VEVENT");
        _lines.Add($"UID:{e.Uid}");
        _lines.Add($"DTSTAMP:{_timestamp}");
        _lines.Add($"DTSTART;TZID={IcsConstants.TimeZoneId}:{e.Start}");
        _lines.Add($"DTEND;TZID={IcsConstants.TimeZoneId}:{e.End}");
        _lines.Add($"SUMMARY:{IcsConstants.EscapeText(e.Summary)}");
        _lines.Add($"DESCRIPTION:{IcsConstants.EscapeText(e.Description)}");
        _lines.Add($"LOCATION:{IcsConstants.EscapeText(e.Location)}");
        if (e.RecurrenceRule is { } rule)
        {
            _lines.Add($"RRULE:{rule}");
        }
        _lines.Add("END:VEVENT");
    }

    public async Task WriteTo(Stream stream, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        foreach (var line in Lines())
        {
            foreach (var foldedLine in FoldLine(line))
            {
                sb.Append(foldedLine);
                sb.Append("\r\n");
            }
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(bytes, cancellationToken);

        IEnumerable<string> Lines()
        {
            foreach (var line in _lines)
            {
                yield return line;
            }
            yield return "END:VCALENDAR";
        }
    }

    // RFC 5545 3.1: physical lines are limited to 75 octets,
    // longer logical lines are folded with CRLF + a single space.
    private static IEnumerable<string> FoldLine(string line)
    {
        const int maxOctets = 75;
        if (Encoding.UTF8.GetByteCount(line) <= maxOctets)
        {
            yield return line;
            yield break;
        }

        var sb = new StringBuilder();
        int octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            int runeOctets = rune.Utf8SequenceLength;
            if (octets + runeOctets > maxOctets)
            {
                var chunk = sb.ToString();
                sb.Clear();
                yield return chunk;

                sb.Append(' ');
                octets = 1;
            }
            sb.Append(rune.ToString());
            octets += runeOctets;
        }
        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }
}

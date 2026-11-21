using System.Diagnostics;
using System.Globalization;
using ScheduleLib.Builders;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing;

public static class ScheduleDocumentParserHelper
{
    public readonly record struct DateInterval(DateTime Start, DateTime End);

    private static bool HasTimeRangeSeparator(ref SequenceReader reader)
    {
        if (reader.IsEmpty)
        {
            return false;
        }
        if (reader.Current == '-')
        {
            return true;
        }
        return false;
    }

    extension(ref SequenceReader reader)
    {
        public DateInterval ParseDateInterval(string format)
        {
            reader.SkipWhitespace();
            var bparser = reader.BufferedView();
            var skipped = bparser.SkipUntilAny(['–', '-', '—']);
            if (skipped.EndOfInput)
            {
                throw InvalidScheduleDocumentException.ForMissingDateIntervalSeparator(reader.ToString());
            }

            var startSpan = reader.PeekSpanUntilPosition(bparser.Position);
            var startDate = ParseDateTime(startSpan, DateIntervalEndpoint.Start);

            bparser.Move();
            reader.MoveTo(bparser.Position);

            var endSpan = reader.PeekSpanUntilEnd();
            var endDate = ParseDateTime(endSpan, DateIntervalEndpoint.End);

            if (startDate >= endDate)
            {
                throw InvalidScheduleDocumentException.ForInvertedDateInterval(
                    start: startDate,
                    end: endDate);
            }

            return new(Start: startDate, End: endDate);

            DateTime ParseDateTime(ReadOnlySpan<char> s, DateIntervalEndpoint endpoint)
            {
                var culture = CultureInfo.InvariantCulture;
                Debug.Assert(
                    culture.Calendar.TwoDigitYearMax == 2049,
                    "Fix this if you want to parse older docs");

                if (!DateTime.TryParseExact(
                        s: s,
                        format: format,
                        provider: culture,
                        style: default,
                        result: out var date))
                {
                    throw InvalidScheduleDocumentException.ForInvalidIntervalEndpointDate(
                        endpoint: endpoint,
                        dateText: s.ToString(),
                        format: format);
                }
                return date;
            }
        }

        public TimeInterval ParseTimeInterval(bool allowOpenInterval = false)
        {
            // HH:MM-HH:MM
            reader.SkipWhitespace();
            if (ParserHelper.ParseTime(ref reader) is not { } startTime)
            {
                throw InvalidScheduleDocumentException.ForMissingTimeRangeStart(reader.ToString());
            }
            if (!HasTimeRangeSeparator(ref reader))
            {
                if (allowOpenInterval)
                {
                    return TimeInterval.CreateOpenInterval(startTime);
                }
                throw InvalidScheduleDocumentException.ForMissingTimeRangeSeparator(reader.ToString());
            }
            reader.Move();

            if (ParserHelper.ParseTime(ref reader) is not { } endTime)
            {
                throw InvalidScheduleDocumentException.ForMissingTimeRangeEnd(reader.ToString());
            }

            reader.SkipWhitespace();

            if (!reader.IsEmpty)
            {
                throw InvalidScheduleDocumentException.ForTrailingTimeRangeText(reader.ToString());
            }

            return new(Start: startTime, End: endTime);
        }

        public DayOfWeek ParseDayOfWeek(DayNameParser dayNameParser)
        {
            var bparser = reader.BufferedView();
            var skipResult = bparser.SkipLetters();
            if (!skipResult.SkippedAny)
            {
                throw InvalidScheduleDocumentException.ForMissingDayName();
            }

            var dayOfWeekSpan = reader.PeekSpanUntilPosition(bparser.Position);
            if (dayNameParser.Map(dayOfWeekSpan) is not { } day1)
            {
                throw InvalidScheduleDocumentException.ForUnknownDayName(dayOfWeekSpan);
            }

            reader.MoveTo(bparser.Position);
            return day1;
        }

        public DateOnly ParseDate(ReadOnlySpan<char> format)
        {
            var bparser = reader.BufferedView();
            var result = bparser.Skip(new SkipDate());
            if (!result.SkippedAny)
            {
                throw InvalidScheduleDocumentException.ForCouldNotParseDate(reader);
            }

            var dateSpan = reader.PeekSpanUntilPosition(bparser.Position);
            bool parsed = DateOnly.TryParseExact(
                s: dateSpan,
                format: format,
                provider: CultureInfo.InvariantCulture,
                style: DateTimeStyles.None,
                out var date);
            if (!parsed)
            {
                throw InvalidScheduleDocumentException.ForDateNotMatchingFormat();
            }

            reader.MoveTo(bparser.Position);
            return date;
        }
    }

    private struct SkipDate : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (ch == '.')
            {
                return true;
            }

            if (char.IsNumber(ch))
            {
                return true;
            }

            return false;
        }
    }
}

public readonly record struct TimeInterval(TimeOnly Start, TimeOnly End)
{
    public static TimeInterval CreateOpenInterval(TimeOnly start)
    {
        return new(Start: start, End: default);
    }

    public bool IsOpenInterval => End == default;
}

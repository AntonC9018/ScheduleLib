using System.Diagnostics;
using System.Globalization;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Parsing;

public static class ScheduleDocumentParserHelper
{
    extension(ref SequenceReader reader)
    {
        public (DateTime Start, DateTime End) ParseDateInterval(string format)
        {
            reader.SkipWhitespace();
            var bparser = reader.BufferedView();
            var skipped = bparser.SkipUntilAny(['–', '-', '—']);
            if (skipped.EndOfInput)
            {
                throw new NotSupportedException("Expected interval separator");
            }

            var startSpan = reader.PeekSpanUntilPosition(bparser.Position);
            var startDate = ParseDateTime(startSpan, "Invalid start date");

            bparser.Move();
            reader.MoveTo(bparser.Position);

            var endSpan = reader.PeekSpanUntilEnd();
            var endDate = ParseDateTime(endSpan, "Invalid end date");

            if (startDate >= endDate)
            {
                throw new NotSupportedException("The start date must be before the end date");
            }

            return (startDate, endDate);

            DateTime ParseDateTime(ReadOnlySpan<char> s, string error)
            {
                var culture = CultureInfo.CurrentCulture;
                Debug.Assert(culture.Calendar.TwoDigitYearMax == 2049,
                    "Fix this if you want to parse older docs");

                if (!DateTime.TryParseExact(
                        s: s,
                        format: format,
                        provider: culture,
                        style: default,
                        result: out var date))
                {
                    throw new NotSupportedException(error);
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
                throw new NotSupportedException("Expected time range start");
            }
            if (reader.IsEmpty || reader.Current != '-')
            {
                if (allowOpenInterval)
                {
                    return TimeInterval.CreateOpenInterval(startTime);
                }
                throw new NotSupportedException("Expected '-' after start time");
            }
            reader.Move();

            if (ParserHelper.ParseTime(ref reader) is not { } endTime)
            {
                throw new NotSupportedException("Expected time range end");
            }

            reader.SkipWhitespace();

            if (!reader.IsEmpty)
            {
                throw new NotSupportedException("Time range not consumed fully");
            }

            return new(startTime, endTime);
        }

        public DayOfWeek ParseDayOfWeek(DayNameParser dayNameParser)
        {
            var bparser = reader.BufferedView();
            var skipResult = bparser.SkipLetters();
            if (!skipResult.SkippedAny)
            {
                throw new InvalidOperationException("Expected the day name");
            }

            var dayOfWeekSpan = reader.PeekSpanUntilPosition(bparser.Position);
            if (dayNameParser.Map(dayOfWeekSpan) is not { } day1)
            {
                throw new InvalidOperationException($"Unknown day name: `{dayOfWeekSpan}`");
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
                throw new InvalidOperationException($"Could not parse the date in string `{reader}`");
            }

            var dateSpan = reader.PeekSpanUntilPosition(bparser.Position);
            bool parsed = DateOnly.TryParseExact(dateSpan, format, out var date);
            if (!parsed)
            {
                throw new InvalidOperationException("Date not parsed according to the format.");
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
        return new(start, default);
    }

    public bool IsOpenInterval => End == default;
}

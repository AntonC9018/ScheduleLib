using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Builders;

public abstract class InvalidGroupNameException : ScheduleBuildException
{
    protected InvalidGroupNameException(string message)
        : base(message)
    {
    }

    public static InvalidGroupNameException ForNameTooShort(int minimumLength, int actualLength)
    {
        return new NameTooShortException(
            minimumLength: minimumLength,
            actualLength: actualLength);
    }

    public static InvalidGroupNameException ForUnrecognizedLanguage(char value)
    {
        return new UnrecognizedLanguageException(value);
    }

    public static InvalidGroupNameException ForUnclosedLanguageParenthesis()
    {
        return new GeneralException("Unclosed parenthesis in the language.");
    }

    public static InvalidGroupNameException ForUnrecognizedLanguageString()
    {
        return new GeneralException("Unrecognized language string");
    }

    public static InvalidGroupNameException ForMissingLabel()
    {
        return new GeneralException("Must be prefixed with at least one letter indicating the group.");
    }

    public static InvalidGroupNameException ForMissingNumberAfterLabel()
    {
        return new GeneralException("After the label, it must include a number!");
    }

    public static InvalidGroupNameException ForInvalidGroupNumberLength(int expectedLength)
    {
        return new InvalidGroupNumberLengthException(
            expectedLength);
    }

    public static InvalidGroupNameException ForInvalidYearLength(int expectedLength)
    {
        return new InvalidGroupYearLengthException(
            expectedLength);
    }

    public static InvalidGroupNameException ForInvalidYear()
    {
        return new GeneralException("Must be a valid year that has 2 letters.");
    }

    public sealed class NameTooShortException : InvalidGroupNameException
    {
        internal NameTooShortException(int minimumLength, int actualLength)
            : base($"The minimum length of the name is {minimumLength}")
        {
            MinimumLength = minimumLength;
            ActualLength = actualLength;
        }

        public int MinimumLength { get; }
        public int ActualLength { get; }
    }

    public sealed class UnrecognizedLanguageException : InvalidGroupNameException
    {
        internal UnrecognizedLanguageException(char value)
            : base($"Unrecognized language: {value}")
        {
            Value = value;
        }

        public char Value { get; }
    }

    public sealed class InvalidGroupNumberLengthException : InvalidGroupNameException
    {
        internal InvalidGroupNumberLengthException(int expectedLength)
            : base($"String must include {expectedLength} letters of the group after the year.")
        {
            ExpectedLength = expectedLength;
        }

        public int ExpectedLength { get; }
    }

    public sealed class InvalidGroupYearLengthException : InvalidGroupNameException
    {
        internal InvalidGroupYearLengthException(int expectedLength)
            : base($"String must include {expectedLength} letters of the year after the label.")
        {
            ExpectedLength = expectedLength;
        }

        public int ExpectedLength { get; }
    }

    public sealed class GeneralException : InvalidGroupNameException
    {
        internal GeneralException(string message)
            : base(message)
        {
        }
    }
}

public abstract class InvalidScheduleDocumentException : ScheduleBuildException
{
    protected InvalidScheduleDocumentException(string message)
        : base(message)
    {
    }

    public static InvalidScheduleDocumentException ForUnknownTimeSlot(TimeOnly start)
    {
        return new UnknownTimeSlotException(start);
    }

    public static InvalidScheduleDocumentException ForMissingDayName()
    {
        return new GeneralException("Expected the day name");
    }

    public static InvalidScheduleDocumentException ForUnknownDayName(ReadOnlySpan<char> dayName)
    {
        string dayNameText = dayName.ToString();
        return new UnknownDayNameException(dayNameText);
    }

    public static InvalidScheduleDocumentException ForCouldNotParseDate(SequenceReader reader)
    {
        string remainingDateText = reader.ToString();
        return new CouldNotParseDateException(
            remainingDateText);
    }

    public static InvalidScheduleDocumentException ForDateNotMatchingFormat()
    {
        return new GeneralException("Date not parsed according to the format.");
    }

    public static InvalidScheduleDocumentException ForMissingTimeAfterSlot()
    {
        return new GeneralException("Expected time after the time slot");
    }

    public static InvalidScheduleDocumentException ForSemesterWithoutRoman()
    {
        return new GeneralException("Sem must be followed by a roman numeral");
    }

    public static InvalidScheduleDocumentException ForTrailingAfterSemesterRoman()
    {
        return new GeneralException("Roman numeral after sem must be the last thing");
    }

    public static InvalidScheduleDocumentException ForMissingDateIntervalSeparator(string text)
    {
        return new InvalidDateIntervalSyntaxException(
            rawText: text,
            error: DateIntervalError.MissingSeparator);
    }

    public static InvalidScheduleDocumentException ForInvalidIntervalEndpointDate(
        DateIntervalEndpoint endpoint,
        string dateText,
        string format)
    {
        return new InvalidIntervalEndpointDateException(
            endpoint: endpoint,
            dateText: dateText,
            expectedFormat: format);
    }

    public static InvalidScheduleDocumentException ForInvertedDateInterval(DateTime start, DateTime end)
    {
        return new InvertedDateIntervalException(
            start: start,
            end: end);
    }

    public static InvalidScheduleDocumentException ForMissingTimeRangeStart(string remainingText)
    {
        return new InvalidTimeIntervalSyntaxException(
            error: TimeIntervalError.MissingStart,
            remainingText: remainingText);
    }

    public static InvalidScheduleDocumentException ForMissingTimeRangeSeparator(string remainingText)
    {
        return new InvalidTimeIntervalSyntaxException(
            error: TimeIntervalError.MissingSeparator,
            remainingText: remainingText);
    }

    public static InvalidScheduleDocumentException ForMissingTimeRangeEnd(string remainingText)
    {
        return new InvalidTimeIntervalSyntaxException(
            error: TimeIntervalError.MissingEnd,
            remainingText: remainingText);
    }

    public static InvalidScheduleDocumentException ForTrailingTimeRangeText(string remainingText)
    {
        return new InvalidTimeIntervalSyntaxException(
            error: TimeIntervalError.TrailingText,
            remainingText: remainingText);
    }

    public sealed class UnknownTimeSlotException : InvalidScheduleDocumentException
    {
        internal UnknownTimeSlotException(TimeOnly start)
            : base($"Unknown lesson start time: {start}")
        {
            Start = start;
        }

        public TimeOnly Start { get; }
    }

    public sealed class UnknownDayNameException : InvalidScheduleDocumentException
    {
        internal UnknownDayNameException(string dayName)
            : base($"Unknown day name: `{dayName}`")
        {
            DayName = dayName;
        }

        public string DayName { get; }
    }

    public sealed class CouldNotParseDateException : InvalidScheduleDocumentException
    {
        internal CouldNotParseDateException(string remainingDateText)
            : base($"Could not parse the date in string `{remainingDateText}`")
        {
            RemainingDateText = remainingDateText;
        }

        public string RemainingDateText { get; }
    }

    public sealed class InvalidDateIntervalSyntaxException : InvalidScheduleDocumentException
    {
        internal InvalidDateIntervalSyntaxException(
            string rawText,
            DateIntervalError error)
            : base(CreateMessage(rawText, error))
        {
            RawText = rawText;
            Error = error;
        }

        public string RawText { get; }
        public DateIntervalError Error { get; }

        private static string CreateMessage(string rawText, DateIntervalError error)
        {
            return error switch
            {
                DateIntervalError.MissingSeparator => $"Expected interval separator in '{rawText}'.",
                _ => $"Invalid date interval syntax '{rawText}' ({error}).",
            };
        }
    }

    public sealed class InvalidIntervalEndpointDateException : InvalidScheduleDocumentException
    {
        internal InvalidIntervalEndpointDateException(
            DateIntervalEndpoint endpoint,
            string dateText,
            string expectedFormat)
            : base($"Invalid {endpoint} date '{dateText}' for format '{expectedFormat}'.")
        {
            Endpoint = endpoint;
            DateText = dateText;
            ExpectedFormat = expectedFormat;
        }

        public DateIntervalEndpoint Endpoint { get; }
        public string DateText { get; }
        public string ExpectedFormat { get; }
    }

    public sealed class InvertedDateIntervalException : InvalidScheduleDocumentException
    {
        internal InvertedDateIntervalException(DateTime start, DateTime end)
            : base($"The start date ({start}) must be before the end date ({end}).")
        {
            Start = start;
            End = end;
        }

        public DateTime Start { get; }
        public DateTime End { get; }
    }

    public sealed class InvalidTimeIntervalSyntaxException : InvalidScheduleDocumentException
    {
        internal InvalidTimeIntervalSyntaxException(
            TimeIntervalError error,
            string remainingText)
            : base(CreateMessage(error, remainingText))
        {
            Error = error;
            RemainingText = remainingText;
        }

        public TimeIntervalError Error { get; }
        public string RemainingText { get; }

        private static string CreateMessage(TimeIntervalError error, string remainingText)
        {
            return error switch
            {
                TimeIntervalError.MissingStart => $"Expected time range start in '{remainingText}'.",
                TimeIntervalError.MissingSeparator => $"Expected '-' after start time in '{remainingText}'.",
                TimeIntervalError.MissingEnd => $"Expected time range end in '{remainingText}'.",
                TimeIntervalError.TrailingText => $"Time range not consumed fully; trailing '{remainingText}'.",
                _ => $"Invalid time range '{remainingText}' ({error}).",
            };
        }
    }

    public sealed class GeneralException : InvalidScheduleDocumentException
    {
        internal GeneralException(string message)
            : base(message)
        {
        }
    }
}

public enum DateIntervalError
{
    MissingSeparator,
}

public enum DateIntervalEndpoint
{
    Start,
    End,
}

public enum TimeIntervalError
{
    MissingStart,
    MissingSeparator,
    MissingEnd,
    TrailingText,
}

/// <summary>
/// The course-name parser configuration is malformed: ignored shortened words
/// must be given without the trailing dot.
/// </summary>
public sealed class InvalidCourseNameConfigException : ScheduleBuildException
{
    private InvalidCourseNameConfigException(string message)
        : base(message)
    {
    }

    public static InvalidCourseNameConfigException ForIgnoredShortenedWordWithDot()
    {
        return new("Just provide the words without the dot.");
    }
}

using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Builders;

/// <summary>
/// A group name does not follow the expected label/year/number/language shape.
/// </summary>
public abstract class InvalidGroupNameException : ScheduleBuildException
{
    protected InvalidGroupNameException(string message)
        : base(message)
    {
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

/// <summary>
/// A schedule document header or cell does not follow the expected
/// day/date/time/semester shape.
/// </summary>
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

    public sealed class GeneralException : InvalidScheduleDocumentException
    {
        internal GeneralException(string message)
            : base(message)
        {
        }
    }
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

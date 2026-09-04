using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Builders;

/// <summary>
/// A group name does not follow the expected label/year/number/language shape.
/// </summary>
public sealed class InvalidGroupNameException : ScheduleBuildException
{
    private InvalidGroupNameException(string message)
        : base(message)
    {
    }

    public static InvalidGroupNameException ForFrenchAndDual()
    {
        return new("Both FR and DUAL parsed, not allowed.");
    }

    public static InvalidGroupNameException ForUnrecognizedLanguage(char value)
    {
        return new($"Unrecognized language: {value}");
    }

    public static InvalidGroupNameException ForUnclosedLanguageParenthesis()
    {
        return new("Unclosed parenthesis in the language.");
    }

    public static InvalidGroupNameException ForUnrecognizedLanguageString()
    {
        return new("Unrecognized language string");
    }

    public static InvalidGroupNameException ForMissingLabel()
    {
        return new("Must be prefixed with at least one letter indicating the group.");
    }

    public static InvalidGroupNameException ForMissingNumberAfterLabel()
    {
        return new("After the label, it must include a number!");
    }

    public static InvalidGroupNameException ForInvalidGroupNumberLength(int expectedLength)
    {
        return new($"String must include {expectedLength} letters of the group after the year.");
    }

    public static InvalidGroupNameException ForInvalidYearLength(int expectedLength)
    {
        return new($"String must include {expectedLength} letters of the year after the label.");
    }

    public static InvalidGroupNameException ForInvalidYear()
    {
        return new("Must be a valid year that has 2 letters.");
    }
}

/// <summary>
/// A schedule document header or cell does not follow the expected
/// day/date/time/semester shape.
/// </summary>
public sealed class InvalidScheduleDocumentException : ScheduleBuildException
{
    private InvalidScheduleDocumentException(string message)
        : base(message)
    {
    }

    public static InvalidScheduleDocumentException ForMissingDayName()
    {
        return new("Expected the day name");
    }

    public static InvalidScheduleDocumentException ForUnknownDayName(ReadOnlySpan<char> dayName)
    {
        return new($"Unknown day name: `{dayName}`");
    }

    public static InvalidScheduleDocumentException ForCouldNotParseDate(SequenceReader reader)
    {
        return new($"Could not parse the date in string `{reader}`");
    }

    public static InvalidScheduleDocumentException ForDateNotMatchingFormat()
    {
        return new("Date not parsed according to the format.");
    }

    public static InvalidScheduleDocumentException ForMissingTimeAfterSlot()
    {
        return new("Expected time after the time slot");
    }

    public static InvalidScheduleDocumentException ForSemesterWithoutRoman()
    {
        return new("Sem must be followed by a roman numeral");
    }

    public static InvalidScheduleDocumentException ForTrailingAfterSemesterRoman()
    {
        return new("Roman numeral after sem must be the last thing");
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

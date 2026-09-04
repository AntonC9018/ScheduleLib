namespace ScheduleLib.Builders;

// Thrown by the ScheduleLib.Dates assembly (semester intervals, parity
// documents). Defined here, next to the other build errors, so Core never has
// to reference Dates.

/// <summary>
/// A semester date-range configuration leaves a required value unspecified:
/// semester, attendance mode, qualification type, start/end date, or year.
/// </summary>
public sealed class IncompleteSemesterDateRangeException : ScheduleBuildException
{
    private IncompleteSemesterDateRangeException(string message)
        : base(message)
    {
    }

    public static IncompleteSemesterDateRangeException ForSemesterNotSpecified()
    {
        return new("Semester not specified");
    }

    public static IncompleteSemesterDateRangeException ForAttendanceModeNotSpecified()
    {
        return new("AttendanceMode not specified");
    }

    public static IncompleteSemesterDateRangeException ForQualificationTypeNotSpecified()
    {
        return new("QualificationType not specified");
    }

    public static IncompleteSemesterDateRangeException ForStartDateNotSpecified()
    {
        return new("Start date not specified");
    }

    public static IncompleteSemesterDateRangeException ForEndDateNotSpecified()
    {
        return new("End date not specified");
    }

    public static IncompleteSemesterDateRangeException ForYearNotSpecified()
    {
        return new("Year not specified");
    }
}

/// <summary>
/// A semester date-range configuration ends before it starts.
/// </summary>
public sealed class InvalidSemesterDateRangeException : ScheduleBuildException
{
    private InvalidSemesterDateRangeException(string message)
        : base(message)
    {
    }

    public static InvalidSemesterDateRangeException ForStartAfterEnd()
    {
        return new("Start date is after end date");
    }
}

/// <summary>
/// Two semester date-range configurations cover the same key.
/// </summary>
public sealed class DuplicateSemesterDateRangeException : ScheduleBuildException
{
    private DuplicateSemesterDateRangeException(string message)
        : base(message)
    {
    }

    public static DuplicateSemesterDateRangeException ForDuplicate(string key)
    {
        return new($"Two range configurations found for the same key: {key}");
    }
}

/// <summary>
/// The study-week parity document has no usable document or body.
/// </summary>
public sealed class InvalidParityDocumentException : ScheduleBuildException
{
    private InvalidParityDocumentException(string message)
        : base(message)
    {
    }

    public static InvalidParityDocumentException ForNoDocument()
    {
        return new("No document found.");
    }

    public static InvalidParityDocumentException ForNoBody()
    {
        return new("No body found.");
    }
}

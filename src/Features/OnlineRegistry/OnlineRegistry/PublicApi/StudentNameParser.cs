using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry;

/// <summary>
/// Parses student names for attendance matching, discarding the patronymic.
/// </summary>
/// <remarks>
/// Attendance workbooks and registry rosters record patronymics inconsistently
/// (present on one side, absent or misspelled on the other), so students are
/// identified by last and first name only.
/// </remarks>
public static class StudentNameParser
{
    public static Name Parse(string text)
    {
        var name = NameHelper.Parse(text);
        return WithoutPatronymic(name);
    }

    public static Name Parse(ref SequenceReader reader)
    {
        var name = NameHelper.Parse(ref reader);
        return WithoutPatronymic(name);
    }

    public static Name? TryParse(ref SequenceReader reader)
    {
        if (NameHelper.TryParseName(ref reader) is not { } name)
        {
            return null;
        }
        return WithoutPatronymic(name);
    }

    private static Name WithoutPatronymic(Name name)
    {
        var fields = name.Fields with
        {
            Patronymic = default,
        };
        return new Name(fields);
    }
}

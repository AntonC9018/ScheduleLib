namespace ScheduleLib.Import.Pdf;

public sealed class PdfScheduleColumnsMissingException(string source, int page)
    : FormatException($"{source}, page {page}: expected day, time and group columns.")
{
    public string SourceName { get; } = source;
    public int Page { get; } = page;
}

public sealed class PdfScheduleGroupHeaderMissingException(string source, int page)
    : FormatException($"{source}, page {page}: missing group header.")
{
    public string SourceName { get; } = source;
    public int Page { get; } = page;
}

public sealed class UnknownPdfScheduleDayException(string source, int page, string dayText)
    : FormatException($"{source}, page {page}: unknown day {dayText}.")
{
    public string SourceName { get; } = source;
    public int Page { get; } = page;
    public string DayText { get; } = dayText;
}

public sealed class UnknownPdfScheduleTimeException(string source, int page, string timeText)
    : FormatException($"{source}, page {page}: unknown time {timeText}.")
{
    public string SourceName { get; } = source;
    public int Page { get; } = page;
    public string TimeText { get; } = timeText;
}

public sealed class UnmappedPdfScheduleCellException(
    string source,
    int page,
    PdfBox box,
    string text)
    : FormatException($"{source}, page {page}: unmapped cell {box}: {text}")
{
    public string SourceName { get; } = source;
    public int Page { get; } = page;
    public PdfBox Box { get; } = box;
    public string Text { get; } = text;
}

public sealed class UnknownImportedPdfScheduleDayException(string day)
    : FormatException($"Unknown day: {day}")
{
    public string Day { get; } = day;
}

public sealed class UnknownImportedPdfScheduleStartTimeException(string startTime)
    : FormatException($"Unknown start time: {startTime}")
{
    public string StartTime { get; } = startTime;
}

public sealed class PdfScheduleCellImportException : FormatException
{
    private sealed record Context(
        string Source,
        int Page,
        string Day,
        string Start,
        IReadOnlyList<string> Groups,
        PdfBox Bounds,
        string Text);

    public PdfScheduleCellImportException(PdfScheduleCell cell, Exception innerException)
        : this(Capture(cell), innerException)
    {
    }

    private PdfScheduleCellImportException(Context context, Exception innerException)
        : base(CreateMessage(context), innerException)
    {
        SourceName = context.Source;
        Page = context.Page;
        Day = context.Day;
        Start = context.Start;
        Groups = context.Groups;
        Bounds = context.Bounds;
        Text = context.Text;
    }

    public string SourceName { get; }
    public int Page { get; }
    public string Day { get; }
    public string Start { get; }
    public IReadOnlyList<string> Groups { get; }
    public PdfBox Bounds { get; }
    public string Text { get; }

    private static Context Capture(PdfScheduleCell cell)
    {
        var groups = cell.Groups.ToArray();
        var groupSnapshot = Array.AsReadOnly(groups);
        return new Context(
            Source: cell.Source,
            cell.Page,
            Day: cell.Day,
            Start: cell.Start,
            groupSnapshot,
            cell.Bounds,
            Text: cell.Text);
    }

    private static string CreateMessage(Context context)
    {
        return
            $"{context.Source}, page {context.Page}, {context.Day} {context.Start}, {string.Join(", ", context.Groups)} at {context.Bounds}:\n{context.Text}";
    }
}

public sealed class UnknownPdfCohortCourseException(string cellText)
    : FormatException($"Unknown cohort course: {cellText}")
{
    public string CellText { get; } = cellText;
}

public sealed class UnknownPdfCohortMembershipException(string line)
    : FormatException($"Unknown cohort membership: {line}")
{
    public string Line { get; } = line;
}

public sealed class PdfScheduleTableNotFoundException(int pageNumber)
    : FormatException($"Page {pageNumber}: no bordered schedule table found.")
{
    public int PageNumber { get; } = pageNumber;
}

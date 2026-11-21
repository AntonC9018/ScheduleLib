using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace QuizModels;

public static class MoodleQuizHtmlParser
{
    private readonly record struct QuestionColumn(int SlotNumber, int ColumnIndex);

    private static readonly long MaxTimeSpanSeconds = TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond;

    private static readonly CultureInfo RomanianCulture = CultureInfo.GetCultureInfo("ro-RO");

    private static readonly string[] DateTimeFormats =
    [
        "d MMMM yyyy  HH:mm",
        "dd MMMM yyyy  HH:mm",
        "d MMMM yyyy HH:mm",
        "dd MMMM yyyy HH:mm",
        "d/M/yy, HH:mm",
        "dd/MM/yy, HH:mm",
    ];

    public static List<MoodleBreadcrumbItem> ParseBreadcrumbs(IDocument page)
    {
        var items = page.QuerySelectorAll<IHtmlListItemElement>("nav > ol.breadcrumb > li");
        var path = new List<MoodleBreadcrumbItem>();
        foreach (var item in items)
        {
            path.Add(ReadBreadcrumb(item));
        }
        return path;
    }

    public static ParsedAttemptsTable ParseAttemptsTable(
        IHtmlTableElement table,
        CancellationToken cancellationToken = default)
    {
        var columnIndices = ReadColumnIndices(table);
        if (columnIndices is null)
        {
            throw new MoodleAttemptsTableHeaderNotFoundException();
        }

        var questionColumns = ReadQuestionColumns(columnIndices);
        var attempts = new List<QuizAttempt>();
        var parseIssues = new List<QuizAttemptParseIssue>();
        foreach (var row in table.QuerySelectorAll<IHtmlTableRowElement>("tbody tr"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipRow(row))
            {
                continue;
            }

            if (TryParseAttemptRow(row, columnIndices, questionColumns, parseIssues) is { } attempt)
            {
                attempts.Add(attempt);
            }
        }

        return new ParsedAttemptsTable(Attempts: attempts, ParseIssues: parseIssues);
    }

    public static QuestionStatus ParseQuestionStatus(string? classAttribute)
    {
        if (string.IsNullOrWhiteSpace(classAttribute))
        {
            return QuestionStatus.Unknown;
        }

        var tokens = classAttribute.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (token.Equals("incorrect", StringComparison.Ordinal))
            {
                return QuestionStatus.Incorrect;
            }
            if (token.Equals("partiallycorrect", StringComparison.Ordinal))
            {
                return QuestionStatus.PartiallyCorrect;
            }
            if (token.Equals("requiresgrading", StringComparison.Ordinal))
            {
                return QuestionStatus.NotGraded;
            }
            if (token.Equals("correct", StringComparison.Ordinal))
            {
                return QuestionStatus.Correct;
            }
        }

        return QuestionStatus.Unknown;
    }

    public static DateTime? ParseDateTime(string? dateTime)
    {
        if (string.IsNullOrWhiteSpace(dateTime))
        {
            return null;
        }

        var trimmed = dateTime.Trim();
        if (trimmed is "-" or "–")
        {
            return null;
        }
        if (DateTime.TryParseExact(
                trimmed,
                DateTimeFormats,
                RomanianCulture,
                DateTimeStyles.None,
                out var result))
        {
            return result;
        }

        if (DateTime.TryParse(
                trimmed,
                RomanianCulture,
                DateTimeStyles.None,
                out result))
        {
            return result;
        }

        throw new MoodleDateTimeParseException(dateTime);
    }

    public static TimeSpan? ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        // Moodle formats durations like "1 zi 2 ore", "1 oră 52 min 41 secunde", or "12 min 5 sec".
        var parts = duration.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
        int days = 0;
        int hours = 0;
        int minutes = 0;
        int seconds = 0;
        bool parsedAny = false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                continue;
            }

            var unit = parts[i + 1];
            if (IsDayUnit(unit))
            {
                days = value;
                parsedAny = true;
                i++;
            }
            else if (IsHourUnit(unit))
            {
                hours = value;
                parsedAny = true;
                i++;
            }
            else if (unit.StartsWith("min", StringComparison.OrdinalIgnoreCase))
            {
                minutes = value;
                parsedAny = true;
                i++;
            }
            else if (unit.StartsWith("sec", StringComparison.OrdinalIgnoreCase))
            {
                seconds = value;
                parsedAny = true;
                i++;
            }
        }

        if (!parsedAny)
        {
            return null;
        }

        const long SecondsPerDay = 86_400;
        const long SecondsPerHour = 3_600;
        const long SecondsPerMinute = 60;

        long totalSeconds =
            ((long) days * SecondsPerDay)
            + ((long) hours * SecondsPerHour)
            + ((long) minutes * SecondsPerMinute)
            + seconds;
        if (totalSeconds > MaxTimeSpanSeconds)
        {
            return null;
        }

        return TimeSpan.FromTicks(totalSeconds * TimeSpan.TicksPerSecond);
    }

    public static float? ParseFloatOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Moodle renders decimals with a comma.
        var normalized = value.Trim().Replace(',', '.');
        if (!float.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var result))
        {
            return null;
        }

        return result;
    }

    private static bool IsDayUnit(string unit)
    {
        if (unit.Equals("zi", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (unit.Equals("zile", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (unit.StartsWith("day", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    private static bool IsHourUnit(string unit)
    {
        if (unit.StartsWith("oră", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (unit.StartsWith("ora", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (unit.StartsWith("ore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (unit.StartsWith("hour", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return false;
    }

    private static MoodleBreadcrumbItem ReadBreadcrumb(IHtmlListItemElement element)
    {
        var anchor = element.QuerySelector<IHtmlAnchorElement>("a");
        if (anchor is not null)
        {
            return new(Name: anchor.TextContent.Trim(), Href: anchor.Href);
        }

        var span = element.QuerySelector<IHtmlSpanElement>("span");
        if (span is not null)
        {
            return new(Name: span.TextContent.Trim(), Href: null);
        }

        throw new InvalidMoodleBreadcrumbException();
    }

    private static Dictionary<string, int>? ReadColumnIndices(IHtmlTableElement table)
    {
        var headerRow = table.QuerySelector("thead tr");
        if (headerRow is null)
        {
            return null;
        }

        var mapping = new Dictionary<string, int>(StringComparer.Ordinal);
        var headers = headerRow.QuerySelectorAll("th");
        for (int i = 0; i < headers.Length; i++)
        {
            var sortBy = headers[i].QuerySelector("a[data-sortby]")
                ?.GetAttribute("data-sortby");
            if (!string.IsNullOrEmpty(sortBy))
            {
                mapping[sortBy] = i;
            }
        }

        return mapping;
    }

    private static bool ShouldSkipRow(IHtmlTableRowElement row)
    {
        if (row.Cells.Length == 0)
        {
            return true;
        }
        if (row.QuerySelector(".tabledivider") is not null)
        {
            return true;
        }
        if (row.TextContent.Contains("Medie generală", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    private static QuizAttempt? TryParseAttemptRow(
        IHtmlTableRowElement row,
        Dictionary<string, int> columnIndices,
        List<QuestionColumn> questionColumns,
        List<QuizAttemptParseIssue> parseIssues)
    {
        var cells = row.Cells;
        var attemptId = ExtractAttemptId(cells[0]);
        var nameCell = FindCell(cells, columnIndices, "firstname", "lastname");
        var userName = nameCell is null ? null : ExtractUserName(nameCell);
        if (string.IsNullOrEmpty(userName))
        {
            if (string.IsNullOrEmpty(attemptId))
            {
                return null;
            }

            throw new MoodleAttemptUserNameMissingException(attemptId);
        }

        var reviewLink = nameCell!.QuerySelector("a.reviewlink")?.GetAttribute("href");
        if (reviewLink is null)
        {
            // Enrolled users with no quiz attempt have a name cell, no attempt checkbox, and no review link.
            if (string.IsNullOrEmpty(attemptId))
            {
                return null;
            }

            throw new MoodleAttemptReviewLinkMissingException(attemptId);
        }

        var stateText = ExtractCellText(FindCell(cells, columnIndices, "state"));
        var state = string.IsNullOrEmpty(stateText)
            ? AttemptState.Unknown
            : AttemptState.Create(stateText);

        var timeStartText = ExtractCellText(FindCell(cells, columnIndices, "timestart"));
        var timeStart = ParseRowDateTime(
            value: timeStartText,
            attemptId: attemptId,
            userName: userName,
            fieldName: "timestart",
            parseIssues: parseIssues);
        var timeFinishText = ExtractCellText(FindCell(cells, columnIndices, "timefinish"));
        var timeFinish = ParseRowDateTime(
            value: timeFinishText,
            attemptId: attemptId,
            userName: userName,
            fieldName: "timefinish",
            parseIssues: parseIssues);

        return new QuizAttempt
        {
            AttemptId = attemptId,
            UserName = userName,
            Email = ExtractCellText(FindCell(cells, columnIndices, "email")),
            State = state,
            TimeStart = timeStart,
            TimeFinish = timeFinish,
            Duration = ParseDuration(ExtractCellText(FindCell(cells, columnIndices, "duration"))),
            Grade = ParseFloatOrNull(ExtractCellText(FindCell(cells, columnIndices, "sumgrades"))),
            ReviewLink = reviewLink,
            QuestionGrades = ExtractQuestionGrades(cells, questionColumns),
        };
    }

    private static DateTime? ParseRowDateTime(
        string? value,
        string attemptId,
        string userName,
        string fieldName,
        List<QuizAttemptParseIssue> parseIssues)
    {
        try
        {
            var parsed = ParseDateTime(value);
            if (parsed is { } dateTime)
            {
                return dateTime;
            }
            return null;
        }
        catch (MoodleDateTimeParseException ex)
        {
            parseIssues.Add(new QuizAttemptParseIssue(
                AttemptId: attemptId,
                UserName: userName,
                FieldName: fieldName,
                RawValue: ex.DateTimeText));
            return null;
        }
    }

    private static IHtmlTableCellElement? FindCell(
        IHtmlCollection<IHtmlTableCellElement> cells,
        Dictionary<string, int> columnIndices,
        params ReadOnlySpan<string> keys)
    {
        foreach (var key in keys)
        {
            if (columnIndices.TryGetValue(key, out int index))
            {
                if (index < cells.Length)
                {
                    return cells[index];
                }
            }
        }
        return null;
    }

    private static string? ExtractUserName(IHtmlTableCellElement nameCell)
    {
        foreach (var node in nameCell.ChildNodes)
        {
            if (node is IElement element && element.ClassList.Contains("reviewlink"))
            {
                continue;
            }
            var text = node.TextContent.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }
        return null;
    }

    private static string ExtractAttemptId(IElement cell)
    {
        var checkbox = cell.QuerySelector("input[type=checkbox]");
        return checkbox?.GetAttribute("value") ?? "";
    }

    private static string? ExtractCellText(IElement? element)
    {
        if (element is null)
        {
            return null;
        }
        return element.TextContent.Trim();
    }

    private static List<QuestionColumn> ReadQuestionColumns(
        Dictionary<string, int> columnIndices)
    {
        const string Prefix = "qsgrade";
        var columns = new List<QuestionColumn>();
        foreach (var (key, columnIndex) in columnIndices)
        {
            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var suffix = key.AsSpan(Prefix.Length);
            if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int slotNumber))
            {
                continue;
            }
            if (slotNumber <= 0)
            {
                continue;
            }

            columns.Add(new QuestionColumn(
                SlotNumber: slotNumber,
                ColumnIndex: columnIndex));
        }

        columns.Sort(static (left, right) => left.SlotNumber.CompareTo(right.SlotNumber));
        return columns;
    }

    private static List<QuestionGrade> ExtractQuestionGrades(
        IHtmlCollection<IHtmlTableCellElement> cells,
        List<QuestionColumn> questionColumns)
    {
        var grades = new List<QuestionGrade>(questionColumns.Count);
        foreach (var column in questionColumns)
        {
            if (column.ColumnIndex >= cells.Length)
            {
                continue;
            }

            var cell = cells[column.ColumnIndex];
            var link = cell.QuerySelector("a");
            var reviewLink = link?.GetAttribute("href");
            if (reviewLink is null)
            {
                var text = cell.TextContent.Trim();
                if (text is "" or "-" or "–")
                {
                    continue;
                }

                throw new MoodleQuestionReviewLinkMissingException(column.SlotNumber);
            }

            var gradeSpan = cell.QuerySelector(".correct, .incorrect, .partiallycorrect, .requiresgrading");

            grades.Add(new QuestionGrade
            {
                QuestionNumber = column.SlotNumber,
                Grade = ParseFloatOrNull(ExtractCellText(gradeSpan)),
                Status = ParseQuestionStatus(gradeSpan?.GetAttribute("class")),
                ReviewLink = reviewLink,
            });
        }

        return grades;
    }
}

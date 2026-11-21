using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace QuizModels;

public static class MoodleQuizHtmlParser
{
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

    public static List<QuizAttempt> ParseAttemptsTable(
        IHtmlTableElement table,
        CancellationToken cancellationToken = default)
    {
        var columnIndices = ReadColumnIndices(table);
        if (columnIndices is null)
        {
            throw new MoodleAttemptsTableHeaderNotFoundException();
        }

        var attempts = new List<QuizAttempt>();
        foreach (var row in table.QuerySelectorAll<IHtmlTableRowElement>("tbody tr"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ShouldSkipRow(row))
            {
                continue;
            }

            if (TryParseAttemptRow(row, columnIndices) is { } attempt)
            {
                attempts.Add(attempt);
            }
        }

        return attempts;
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

        // Moodle formats durations like "1 oră 52 min 41 secunde" or "12 min 5 sec".
        var parts = duration.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
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
            if (IsHourUnit(unit))
            {
                hours = value;
                parsedAny = true;
            }
            else if (unit.StartsWith("min", StringComparison.OrdinalIgnoreCase))
            {
                minutes = value;
                parsedAny = true;
            }
            else if (unit.StartsWith("sec", StringComparison.OrdinalIgnoreCase))
            {
                seconds = value;
                parsedAny = true;
            }
        }

        if (!parsedAny)
        {
            return null;
        }

        return new TimeSpan(
            hours: hours,
            minutes: minutes,
            seconds: seconds);
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
        return unit.StartsWith("hour", StringComparison.OrdinalIgnoreCase);
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
            var sortBy = headers[i].QuerySelector("a[data-sortby]")?.GetAttribute("data-sortby");
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
        return row.TextContent.Contains("Medie generală", StringComparison.Ordinal);
    }

    private static QuizAttempt? TryParseAttemptRow(
        IHtmlTableRowElement row,
        Dictionary<string, int> columnIndices)
    {
        var cells = row.Cells;
        var nameCell = FindCell(cells, columnIndices, "firstname", "lastname");
        if (nameCell is null)
        {
            return null;
        }

        var userName = ExtractUserName(nameCell);
        if (string.IsNullOrEmpty(userName))
        {
            return null;
        }

        var attemptId = ExtractAttemptId(cells[0]);
        var reviewLink = nameCell.QuerySelector("a.reviewlink")?.GetAttribute("href");
        if (reviewLink is null)
        {
            throw new MoodleAttemptReviewLinkMissingException(attemptId);
        }

        var stateText = ExtractCellText(FindCell(cells, columnIndices, "state"));
        var state = string.IsNullOrEmpty(stateText)
            ? AttemptState.Unknown
            : AttemptState.Create(stateText);

        return new QuizAttempt
        {
            AttemptId = attemptId,
            UserName = userName,
            Email = ExtractCellText(FindCell(cells, columnIndices, "email")),
            State = state,
            TimeStart = ParseDateTime(ExtractCellText(FindCell(cells, columnIndices, "timestart"))),
            TimeFinish = ParseDateTime(ExtractCellText(FindCell(cells, columnIndices, "timefinish"))),
            Duration = ParseDuration(ExtractCellText(FindCell(cells, columnIndices, "duration"))),
            Grade = ParseFloatOrNull(ExtractCellText(FindCell(cells, columnIndices, "sumgrades"))),
            ReviewLink = reviewLink,
            QuestionGrades = ExtractQuestionGrades(cells, columnIndices),
        };
    }

    private static IHtmlTableCellElement? FindCell(
        IHtmlCollection<IHtmlTableCellElement> cells,
        Dictionary<string, int> columnIndices,
        params ReadOnlySpan<string> keys)
    {
        foreach (var key in keys)
        {
            if (columnIndices.TryGetValue(key, out int index) && index < cells.Length)
            {
                return cells[index];
            }
        }
        return null;
    }

    private static string? ExtractUserName(IHtmlTableCellElement nameCell)
    {
        if (nameCell.ChildNodes.Length == 0)
        {
            return null;
        }
        return nameCell.ChildNodes[0].TextContent.Trim();
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

    private static List<QuestionGrade> ExtractQuestionGrades(
        IHtmlCollection<IHtmlTableCellElement> cells,
        Dictionary<string, int> columnIndices)
    {
        var grades = new List<QuestionGrade>();
        for (int questionNum = 1; ; questionNum++)
        {
            var cell = FindCell(cells, columnIndices, $"qsgrade{questionNum}");
            if (cell is null)
            {
                break;
            }

            var link = cell.QuerySelector("a");
            var reviewLink = link?.GetAttribute("href")
                ?? throw new MoodleQuestionReviewLinkMissingException(questionNum);
            var gradeSpan = cell.QuerySelector(".correct, .incorrect, .partiallycorrect, .requiresgrading");

            grades.Add(new QuestionGrade
            {
                QuestionNumber = questionNum,
                Grade = ParseFloatOrNull(ExtractCellText(gradeSpan)),
                Status = ParseQuestionStatus(gradeSpan?.GetAttribute("class")),
                ReviewLink = reviewLink,
            });
        }

        return grades;
    }
}

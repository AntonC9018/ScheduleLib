using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace QuizModels;

// claude wrote most of the code.
public static class MoodleQuizScraper
{
    public static async Task<List<QuizAttempt>> ScrapeQuizAttempts(
        this MoodleScrapingContext context,
        string quizId,
        int pageSize = 2000)
    {
        // Navigate to the quiz overview page
        string url = $"{MoodleScrapingContext.Names.BaseUrl}/mod/quiz/report.php?id={quizId}&mode=overview";
        var page = await context.Browser.OpenAsync(url);

        // Set page size
        var pageSizeInput = (IHtmlInputElement) page.QuerySelector("#id_pagesize")!;
        pageSizeInput.Value = pageSize.ToString();

        // Submit the form
        var submitButton = (IHtmlInputElement) page.QuerySelector("#id_submitbutton")!;
        page = await submitButton.Form!.SubmitAsync();

        // Find the attempts table
        var attemptsElement = page.QuerySelector("table#attempts");
        if (attemptsElement is not IHtmlTableElement attemptsTable)
        {
            throw new InvalidOperationException("Attempts table not found");
        }

        // Parse the table
        var attempts = ParseAttemptsTable(attemptsTable);
        return attempts;
    }

    private static List<QuizAttempt> ParseAttemptsTable(IHtmlTableElement table)
    {
        var attempts = new List<QuizAttempt>();
        if (MappedTableHelper.Create(table) is not { } helper)
        {
            throw new InvalidOperationException("Failed to find table in html.");
        }

        // Parse data rows
        var rows = table.QuerySelectorAll("tbody tr").Cast<IHtmlTableRowElement>().ToList();

        foreach (var row in rows)
        {
            // Skip divider and summary rows
            if (row.QuerySelector(".tabledivider") != null ||
                row.TextContent.Contains("Medie generală"))
            {
                continue;
            }

            if (row.Cells.Length == 0)
            {
                continue;
            }

            var rowSearch = helper.SearchRow(row);
            var id = ExtractAttemptId(rowSearch.Cells[0]);
            if (rowSearch.FindCell("firstname", "lastname").Cell is not { } nameCell)
            {
                break;
            }

            string? ExtractUserName()
            {
                var childNodes = nameCell.ChildNodes;
                if (childNodes.Length == 0)
                {
                    return null;
                }
                var node = childNodes[0];
                var ret = node.TextContent.Trim();
                return ret;
            }
            var userName = ExtractUserName();
            if (userName is null)
            {
                break;
            }
            var email = rowSearch.FindCell("email").MaybeExtractText();
            var state = ParseAttemptState(rowSearch.FindCell("state").MaybeExtractText());
            var timeStart = ParseDateTime(rowSearch.FindCell("timestart").MaybeExtractText());
            var timeFinish = ParseDateTime(rowSearch.FindCell("timefinish").MaybeExtractText());
            var duration = ParseDuration(rowSearch.FindCell("duration").MaybeExtractText());
            var grade = ParseGrade(rowSearch.FindCell("sumgrades").MaybeExtractText());
            var reviewLink = ExtractReviewLink(new(nameCell));
            var questionGrades = ExtractQuestionGrades(rowSearch);
            if (reviewLink is null)
            {
                throw new InvalidOperationException("This is the only thing that must be there - the review link!");
            }

            var attempt = new QuizAttempt
            {
                AttemptId = id,
                UserName = userName,
                Email = email,
                State = state,
                TimeStart = timeStart,
                TimeFinish = timeFinish,
                Duration = duration,
                Grade = grade,
                ReviewLink = reviewLink,
                QuestionGrades = questionGrades,
            };

            attempts.Add(attempt);
        }

        return attempts;
    }

    private static string ExtractAttemptId(IElement cell)
    {
        var checkbox = cell.QuerySelector("input[type=checkbox]");
        return checkbox?.GetAttribute("value") ?? "";
    }

    private readonly struct MappedTableHelper
    {
        private readonly Dictionary<string, int> _mapping { get; init; }

        public static MappedTableHelper? Create(IHtmlTableElement table)
        {
            // Find header row to map columns
            var headerRow = table.QuerySelector("thead tr");
            if (headerRow == null)
            {
                return null;
            }

            var columnMapping = new Dictionary<string, int>();
            var headers = headerRow.QuerySelectorAll("th").ToList();

            for (int i = 0; i < headers.Count; i++)
            {
                var sortByLink = headers[i].QuerySelector("a[data-sortby]");
                if (sortByLink != null)
                {
                    var sortBy = sortByLink.GetAttribute("data-sortby");
                    if (!string.IsNullOrEmpty(sortBy))
                    {
                        columnMapping[sortBy] = i;
                    }
                }
            }

            return new()
            {
                _mapping = columnMapping,
            };
        }

        public readonly struct RowSearchHelper
        {
            private readonly MappedTableHelper _tableHelper;
            public readonly IHtmlCollection<IHtmlTableCellElement> Cells;

            public RowSearchHelper(
                MappedTableHelper tableHelper,
                IHtmlCollection<IHtmlTableCellElement> cells)
            {
                _tableHelper = tableHelper;
                Cells = cells;
            }

            public CellSearchResult FindCell(params ReadOnlySpan<string> keys)
            {
                foreach (var key in keys)
                {
                    if (_tableHelper._mapping.TryGetValue(key, out int index) && index < Cells.Length)
                    {
                        return new(Cells[index]);
                    }
                }
                return new(null);
            }
        }

        public RowSearchHelper SearchRow(IHtmlTableRowElement row)
        {
            return new RowSearchHelper(this, row.Cells);
        }
    }

    public readonly record struct CellSearchResult(IHtmlTableCellElement? Cell);

    private static string? MaybeGetFirst(this IElement? element)
    {
        if (element is null)
        {
            return null;
        }
        return element.TextContent.Trim();
    }

    private static string? MaybeExtractText(this IElement? element)
    {
        if (element is null)
        {
            return null;
        }
        return element.TextContent.Trim();
    }

    private static string? MaybeExtractText(this CellSearchResult element)
    {
        return element.Cell.MaybeExtractText();
    }

    private static string? ExtractReviewLink(this CellSearchResult x)
    {
        if (x.Cell is not { } cell)
        {
            return null;
        }
        var link = cell.QuerySelector("a.reviewlink");
        if (link is null)
        {
            return null;
        }
        return link.GetAttribute("href");
    }

    private static List<QuestionGrade> ExtractQuestionGrades(MappedTableHelper.RowSearchHelper row)
    {
        var grades = new List<QuestionGrade>();
        int questionNum = 1;

        while (true)
        {
            var cellResult = row.FindCell($"qsgrade{questionNum}");
            if (cellResult.Cell is not { } cell)
            {
                break;
            }

            var link = cell.QuerySelector("a");
            var gradeSpan = cell.QuerySelector(".correct, .incorrect, .partiallycorrect, .requiresgrading");
            if (gradeSpan is null)
            {
                Console.WriteLine(cell.InnerHtml);
            }

            grades.Add(new QuestionGrade
            {
                QuestionNumber = questionNum,
                Grade = ParseFloatOrNull(gradeSpan.MaybeExtractText()),
                Status = ParseQuestionStatus(gradeSpan?.GetAttribute("class")),
                ReviewLink = link?.GetAttribute("href") ?? throw new InvalidOperationException("Expecting a review link"),
            });
            questionNum++;
        }

        return grades;
    }

    private static AttemptState ParseAttemptState(string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return AttemptState.Unknown;
        }

        return AttemptState.Create(state);
    }

    private static QuestionStatus ParseQuestionStatus(string? classes)
    {
        if (string.IsNullOrEmpty(classes))
        {
            return QuestionStatus.Unknown;
        }

        if (classes.Contains("correct"))
        {
            return QuestionStatus.Correct;
        }

        if (classes.Contains("incorrect"))
        {
            return QuestionStatus.Incorrect;
        }

        if (classes.Contains("partiallycorrect"))
        {
            return QuestionStatus.PartiallyCorrect;
        }

        if (classes.Contains("requiresgrading"))
        {
            return QuestionStatus.NotGraded;
        }

        return QuestionStatus.Unknown;
    }

    private static QuestionType ParseQuestionType(string? classes)
    {
        if (string.IsNullOrEmpty(classes))
        {
            return QuestionType.Unknown;
        }

        var classList = classes.Split(' ');
        foreach (var cls in classList)
        {
            if (cls == "que" || cls.Contains("feedback") || cls.Contains("graded") ||
                cls == "complete" || cls == "correct" || cls == "incorrect" ||
                cls == "partiallycorrect")
            {
                continue;
            }

            return QuestionType.Create(cls);
        }

        return QuestionType.Unknown;
    }

    private static QuestionState ParseQuestionState(string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return QuestionState.Unknown;
        }

        return QuestionState.Create(state);
    }

    private static ScoreOutOf ParseQuestionGrade(string? grade)
    {
        if (grade is null)
        {
            return default;
        }

        // Format: "Marcat 1,50 din 2,00" or "Marcat 2,00 din 2,00"
        if (!grade.StartsWith("Marcat "))
        {
            return default;
        }

        var parts = grade.Replace("Marcat ", "").Split([" din "], StringSplitOptions.None);
        if (parts.Length != 2)
        {
            return default;
        }

        var scored = ParseFloatOrNull(parts[0])!.Value;
        var outOf = ParseFloatOrNull(parts[1])!.Value;
        return new ScoreOutOf(scored, outOf);
    }

    private static HistoryAction ParseHistoryAction(string? action)
    {
        if (string.IsNullOrEmpty(action))
        {
            return HistoryAction.Unknown;
        }
        if (action.StartsWith("Salvat"))
        {
            return HistoryAction.Saved;
        }
        if (action.StartsWith("Notată manual"))
        {
            return HistoryAction.ManuallyGraded;
        }
        return HistoryAction.Create(action);
    }

    private static string? ParseAnswer(string? x)
    {
        if (string.IsNullOrEmpty(x))
        {
            return null;
        }
        {
            const string s = "Salvat:";
            if (x.StartsWith(s))
            {
                return x.AsSpan()[s.Length ..].Trim().ToString();
            }
        }
        return null;
    }

    private static DateTime? ParseDateTime(string? dateTime)
    {
        if (string.IsNullOrWhiteSpace(dateTime))
        {
            return null;
        }

        // Format: "16 octombrie 2025  13:28"
        var formats = new[]
        {
            "d MMMM yyyy  HH:mm",
            "dd MMMM yyyy  HH:mm",
            "d/M/yy, HH:mm",
            "dd/MM/yy, HH:mm",
        };

        var culture = new CultureInfo("ro-RO");

        if (DateTime.TryParseExact(dateTime.Trim(), formats, culture, DateTimeStyles.None, out var result))
        {
            return result;
        }

        if (DateTime.TryParse(dateTime, culture, DateTimeStyles.None, out result))
        {
            return result;
        }

        throw new InvalidOperationException($"Cannot parse datetime: {dateTime}");
    }

    private static TimeSpan? ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return null;
        }

        // Format: "52 min 41 secunde"
        var parts = duration.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        int minutes = 0;
        int seconds = 0;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!int.TryParse(parts[i], out int value))
            {
                continue;
            }

            var unit = parts[i + 1];
            if (unit.StartsWith("min"))
            {
                minutes = value;
            }
            else if (unit.StartsWith("sec"))
            {
                seconds = value;
            }
        }

        return new TimeSpan(0, minutes, seconds);
    }

    private static float? ParseFloatOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Replace comma with period for decimal separator
        value = value.Replace(',', '.');

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        return null;
    }

    private static float? ParseGrade(string? grade)
    {
        var parsed = ParseFloatOrNull(grade);
        return parsed;
    }

    private static AttemptDetails ParseAttemptDetails(IDocument page)
    {
        var details = new AttemptDetails
        {
            Questions = [],
        };

        // Parse summary table
        var summaryTable = page.QuerySelector("table.quizreviewsummary");
        if (summaryTable != null)
        {
            var rows = summaryTable.QuerySelectorAll("tr");
            foreach (var row in rows)
            {
                var header = row.QuerySelector("th").MaybeExtractText();
                var value = row.QuerySelector("td").MaybeExtractText();

                if (header is null || value is null)
                {
                    continue;
                }

                switch (header)
                {
                    case "Status":
                        details.Status = ParseAttemptState(value);
                        break;
                    case "Început":
                        details.StartedAt = ParseDateTime(value);
                        break;
                    case "Finalizat":
                        details.FinishedAt = ParseDateTime(value);
                        break;
                    case "Durată":
                        details.Duration = ParseDuration(value);
                        break;
                    case "Punctaj":
                        details.RawScore = ParseRawScore(value)!.Value;
                        break;
                    case "Notă":
                        details.FinalGrade = ParseFinalGrade(value);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown summary table header: {header}");
                }
            }
        }

        // Parse questions
        var questionDivs = page.QuerySelectorAll("div.que");
        foreach (var questionDiv in questionDivs)
        {
            var question = ParseQuestionDetail(questionDiv);
            if (question != null)
            {
                details.Questions.Add(question);
            }
        }

        return details;
    }

    private static ScoreOutOf? ParseRawScore(string rawScore)
    {
        // Format: "13,50/15,00"
        var parts = rawScore.Split('/');
        if (parts.Length == 2)
        {
            var a = ParseGrade(parts[0])!.Value;
            var b = ParseGrade(parts[1])!.Value;
            return new ScoreOutOf(a, b);
        }
        return null;
    }

    private static float? ParseFinalGrade(string finalGrade)
    {
        // Format: "9,00 din 10,00 (90%)" - extract the first number
        var parts = finalGrade.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0)
        {
            return ParseGrade(parts[0]);
        }
        return null;
    }

    private static QuestionDetail? ParseQuestionDetail(IElement questionDiv)
    {
        // Question number
        var qnoSpan = questionDiv.QuerySelector(".qno");
        var qnoText = qnoSpan?.TextContent.Trim() ?? "";
        var questionNumber = int.TryParse(qnoText, out int qno) ? qno : 0;

        // Question type (from class)
        var classes = questionDiv.GetAttribute("class");
        var questionType = ParseQuestionType(classes);

        // State
        var stateDiv = questionDiv.QuerySelector(".state");
        var state = ParseQuestionState(stateDiv?.TextContent.Trim());

        // Grade
        var gradeDiv = questionDiv.QuerySelector(".grade");
        var grade = ParseQuestionGrade(gradeDiv?.TextContent.Trim());

        // Version badge
        var badge = questionDiv.QuerySelector(".badge");
        var version = badge?.TextContent.Trim() ?? "";

        // Question text
        var qtextDiv = questionDiv.QuerySelector(".qtext");
        var questionText = qtextDiv?.TextContent.Trim() ?? "";

        // Edit link
        var editLink = questionDiv.QuerySelector(".editquestion a");
        var editQuestionLink = editLink?.GetAttribute("href") ?? "";

        // Answer (for essay questions)
        var answerTextarea = questionDiv.QuerySelector("textarea");
        var answer = answerTextarea?.TextContent.Trim();

        // Multiple choice answers
        var answerDivs = questionDiv.QuerySelectorAll(".answer > div");
        List<ChoiceDetail>? choices = null;
        if (answerDivs.Any())
        {
            choices = new List<ChoiceDetail>();
            foreach (var answerDiv in answerDivs)
            {
                var checkbox = answerDiv.QuerySelector("input[type=checkbox]");
                var label = answerDiv.QuerySelector("div[data-region=answer-label]");
                var isCorrect = answerDiv.GetAttribute("class")?.Contains("correct") ?? false;
                var isChecked = checkbox?.HasAttribute("checked") ?? false;
                if (label != null)
                {
                    choices.Add(new ChoiceDetail
                    {
                        Text = label.TextContent.Trim(),
                        IsChecked = isChecked,
                        IsCorrect = isCorrect,
                    });
                }
            }
        }

        // Comment
        var commentDiv = questionDiv.QuerySelector(".comment");
        var comment = commentDiv?.QuerySelector("p")?.TextContent.Trim() ?? "";
        var commentLink = commentDiv?.QuerySelector("a")?.GetAttribute("href") ?? "";

        // Feedback
        var feedbackDiv = questionDiv.QuerySelector(".feedback");
        var feedback = feedbackDiv?.QuerySelector(".specificfeedback")?.TextContent.Trim() ?? "";
        var correctAnswer = feedbackDiv?.QuerySelector(".rightanswer")?.TextContent.Trim() ?? "";

        // Response history
        var historyTable = questionDiv.QuerySelector(".history table");
        List<ResponseHistoryEntry> responseHistory;
        if (historyTable != null)
        {
            responseHistory = ParseResponseHistory(historyTable);
        }
        else
        {
            responseHistory = [];
        }

        return new QuestionDetail
        {
            QuestionNumber = questionNumber,
            QuestionType = questionType,
            State = state,
            ScoreOutOf = grade,
            Version = version,
            QuestionText = questionText,
            EditQuestionLink = editQuestionLink,
            Answer = answer,
            Choices = choices,
            Comment = comment,
            CommentLink = commentLink,
            Feedback = feedback,
            CorrectAnswer = correctAnswer,
            ResponseHistory = responseHistory,
        };
    }

    private static List<ResponseHistoryEntry> ParseResponseHistory(IElement table)
    {
        var history = new List<ResponseHistoryEntry>();
        var rows = table.QuerySelectorAll("tbody tr");

        int stepIndex = 1;
        foreach (var row in rows)
        {
            var cells = row.QuerySelectorAll("td").ToList();
            if (cells.Count >= 5)
            {
                var actionContent = cells[2].TextContent.Trim();
                history.Add(new ResponseHistoryEntry
                {
                    Step = stepIndex++,
                    Time = ParseDateTime(cells[1].TextContent.Trim()),
                    Action = ParseHistoryAction(actionContent),
                    Answer = ParseAnswer(actionContent),
                    State = ParseQuestionState(cells[3].TextContent.Trim()),
                    Points = ParseFloatOrNull(cells[4].TextContent.Trim()),
                });
            }
        }

        return history;
    }

    public static async Task SaveToJsonAsync(List<QuizAttempt> attempts, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        var json = JsonSerializer.Serialize(attempts, options);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<List<QuizAttempt>> LoadFromJsonAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<List<QuizAttempt>>(json) ?? [];
    }
}

// Models
public class QuizAttempt
{
    public string AttemptId { get; set; } = "";
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public AttemptState State { get; set; }
    public DateTime? TimeStart { get; set; }
    public DateTime? TimeFinish { get; set; }
    public TimeSpan? Duration { get; set; }
    public float? Grade { get; set; }
    public string? ReviewLink { get; set; }
    public List<QuestionGrade> QuestionGrades { get; set; } = [];
}

public class QuestionGrade
{
    public int QuestionNumber { get; set; }
    public float? Grade { get; set; }
    public QuestionStatus Status { get; set; }
    public required string ReviewLink { get; set; }
}

public class AttemptDetails
{
    public AttemptState Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public ScoreOutOf RawScore { get; set; }
    public float? FinalGrade { get; set; }
    public List<QuestionDetail> Questions { get; set; } = [];
}

public class QuestionDetail
{
    public int QuestionNumber { get; set; }
    public QuestionType QuestionType { get; set; }
    public QuestionState State { get; set; }
    public ScoreOutOf ScoreOutOf { get; set; }
    public string? Version { get; set; }
    public string? QuestionText { get; set; }
    public required string EditQuestionLink { get; set; }
    public string? Answer { get; set; }
    public List<ChoiceDetail>? Choices { get; set; }
    public string? Comment { get; set; }
    public required string CommentLink { get; set; }
    public string? Feedback { get; set; }
    public string? CorrectAnswer { get; set; }
    public List<ResponseHistoryEntry> ResponseHistory { get; set; } = [];
}

public class ChoiceDetail
{
    public string Text { get; set; } = "";
    public bool IsChecked { get; set; }
    public bool IsCorrect { get; set; }
}

public class ResponseHistoryEntry
{
    public int Step { get; set; }
    public DateTime? Time { get; set; }
    public HistoryAction Action { get; set; }
    public QuestionState State { get; set; }
    public float? Points { get; set; }
    public string? Answer { get; set; }
}

// Enums converted to record structs
public readonly record struct AttemptState(string Value)
{
    public static AttemptState Unknown => new("unknown");
    public static AttemptState Finished => new("Finalizate");
    public static AttemptState InProgress => new("În progres");
    public static AttemptState Abandoned => new("Abandoned");

    public static AttemptState Create(string value)
    {
        return value switch
        {
            "Finalizate" => Finished,
            "În progres" => InProgress,
            "Abandoned" => Abandoned,
            "unknown" => Unknown,
            _ => new AttemptState(value),
        };
    }
}

public readonly record struct QuestionStatus(string Value)
{
    public static QuestionStatus Unknown => new("unknown");
    public static QuestionStatus Correct => new("correct");
    public static QuestionStatus Incorrect => new("incorrect");
    public static QuestionStatus PartiallyCorrect => new("partiallycorrect");
    public static QuestionStatus NotGraded => new("requiresgrading");

    public static QuestionStatus Create(string value)
    {
        return value switch
        {
            "correct" => Correct,
            "incorrect" => Incorrect,
            "partiallycorrect" => PartiallyCorrect,
            "requiresgrading" => NotGraded,
            "unknown" => Unknown,
            _ => new QuestionStatus(value),
        };
    }
}

public readonly record struct HistoryAction(string Value)
{
    public static HistoryAction Unknown => new("unknown");
    public static HistoryAction Started => new("Început");
    public static HistoryAction Saved => new("Salvat");
    public static HistoryAction Finished => new("Încercare finalizată");
    public static HistoryAction ManuallyGraded => new("Notată manual");

    public static HistoryAction Create(string value)
    {
        return value switch
        {
            "Început" => Started,
            "Salvat" => Saved,
            "Încercare finalizată" => Finished,
            "Notată manual" => ManuallyGraded,
            "unknown" => Unknown,
            _ => new HistoryAction(value),
        };
    }
}

public readonly record struct QuestionState(string Value)
{
    public static QuestionState Unknown => new("unknown");
    public static QuestionState Complete => new("Complet");
    public static QuestionState AnswerSaved => new("Răspuns salvat");
    public static QuestionState NotYetAnswered => new("Nu a primit răspuns încă");
    public static QuestionState PartiallyCorrect => new("Parțial corect");
    public static QuestionState Correct => new("Corect");
    public static QuestionState Incorrect => new("Incorect");

    public static QuestionState Create(string value)
    {
        return value switch
        {
            "Complet" => Complete,
            "Răspuns salvat" => AnswerSaved,
            "Nu a primit răspuns încă" => NotYetAnswered,
            "Parțial corect" => PartiallyCorrect,
            "Corect" => Correct,
            "Incorect" => Incorrect,
            "unknown" => Unknown,
            _ => new QuestionState(value),
        };
    }
}

public readonly record struct QuestionType(string Value)
{
    public static QuestionType Unknown => new("unknown");
    public static QuestionType Essay => new("essay");
    public static QuestionType MultiChoice => new("multichoice");
    public static QuestionType TrueFalse => new("truefalse");
    public static QuestionType ShortAnswer => new("shortanswer");
    public static QuestionType Numerical => new("numerical");
    public static QuestionType Matching => new("matching");
    public static QuestionType Calculated => new("calculated");

    public static QuestionType Create(string value)
    {
        return value switch
        {
            "essay" => Essay,
            "multichoice" => MultiChoice,
            "truefalse" => TrueFalse,
            "shortanswer" => ShortAnswer,
            "numerical" => Numerical,
            "matching" => Matching,
            "calculated" => Calculated,
            _ => new QuestionType(value),
        };
    }
}

public readonly record struct ScoreOutOf(float Scored, float OutOf);

// ChatGPT repurposed some other code
public static class MoodleQuizAnswersSerializer
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        options.Converters.Add(new SingleValueWrapperConverterFactory());

        var textEncoder = new TextEncoderSettings();
        textEncoder.AllowRanges(UnicodeRanges.All);
        options.Encoder = JavaScriptEncoder.Create(textEncoder);

        return options;
    }

    public static async Task SaveToJsonAsync(List<QuizAttempt> attempts, string filePath)
    {
        var json = JsonSerializer.Serialize(attempts, Options);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<List<QuizAttempt>> LoadFromJsonAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<List<QuizAttempt>>(json, Options) ?? [];
    }
}

file static class Accessors
{
    private static ConcurrentDictionary<Type, AccessorsBase?> _accessors = new();

    public static Accessors<T, TValue> Get<T, TValue>()
    {
        var t = _accessors[typeof(T)];
        Debug.Assert(t != null);
        Debug.Assert(t.ValueType == typeof(TValue));
        return (Accessors<T, TValue>) t;
    }

    public static AccessorsBase? Get(Type type)
    {
        var t = _accessors[type];
        return t;
    }

    public static AccessorsBase? TryAddAccessorsFor(Type type)
    {
        if (_accessors.TryGetValue(type, out var accessor))
        {
            return accessor;
        }

        var info = FindInfo(type);
        if (info == default)
        {
            _accessors.TryAdd(type, null);
            return null;
        }

        var createMethod = CreateMethod.MakeGenericMethod(type, info.ValueType);
        var ret = (AccessorsBase) createMethod.Invoke(null, [info])!;
        ret = _accessors.GetOrAdd(type, ret);
        return ret;
    }

    private readonly record struct Info(MethodInfo? CreateMethod, ConstructorInfo Constructor, MemberInfo Member, Type ValueType);
    private static Info FindInfo(Type type)
    {
        var constructors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(c => (Params: c.GetParameters(), Constructor: c))
            .ToArray();
        if (constructors.Any(x => x.Params.Length > 1))
        {
            return default;
        }

        var fields = type
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToArray();
        if (fields.Length > 1)
        {
            return default;
        }

        var createMethods = type
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Select(x => (Params: x.GetParameters(), Method: x))
            .Where(x => x.Params.Length == 1)
            .Where(x => x.Method.ReturnType == type)
            .ToArray();

        var constructorsWithOneParam = constructors
            .Where(x => x.Params.Length == 1)
            .ToArray();

        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        // We'll collect (constructor, matchingMember) pairs where matchingMember.Type == constructor param type
        Info wholeMatch = default;

        foreach (var ctor in constructorsWithOneParam)
        {
            var paramType = ctor.Params[0].ParameterType;

            // find members whose type equals the constructor parameter type
            var matchingProps = properties.Where(p => p.PropertyType == paramType).Cast<MemberInfo>();
            var matchingFields = fields.Where(f => f.FieldType == paramType).Cast<MemberInfo>();
            using var matchingMembers = matchingProps.Concat(matchingFields).GetEnumerator();

            if (!matchingMembers.MoveNext())
            {
                continue;
            }
            var match = matchingMembers.Current;
            if (matchingMembers.MoveNext())
            {
                continue;
            }

            if (wholeMatch != default)
            {
                return default;
            }


            var createMethod = createMethods
                .FirstOrDefault(x => x.Params[0].ParameterType == ctor.Params[0].ParameterType);
            wholeMatch = new(createMethod.Method, ctor.Constructor, match, paramType);
        }
        return wholeMatch;
    }

    private static readonly MethodInfo CreateMethod = typeof(Accessors).GetMethod(
        nameof(Create),
        BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Accessors<T, TValue> Create<T, TValue>(Info info)
    {
        Func<TValue?, T> creator;
        if (info.CreateMethod is { } createMethod)
        {
            creator = createMethod.CreateDelegate<Func<TValue?, T>>();

        }
        else
        {
            // Compile: (TValue v) => new T(v)
            var valueParam = Expression.Parameter(typeof(TValue), "value");
            var newExpr = Expression.New(info.Constructor, valueParam);
            creator = Expression.Lambda<Func<TValue?, T>>(newExpr, valueParam).Compile();
        }

        // Compile: (T t) => t.Value
        var tParam = Expression.Parameter(typeof(T), "t");
        Expression memberAccess = info.Member is PropertyInfo prop
            ? Expression.Property(tParam, prop)
            : Expression.Field(tParam, (FieldInfo) info.Member);

        var extractor = Expression.Lambda<Func<T, TValue?>>(memberAccess, tParam).Compile();
        return new()
        {
            Creator = creator,
            Extractor = extractor,
            ValueType = typeof(TValue),
        };
    }
}

file class AccessorsBase
{
    public required Type ValueType { get; init; }
}

file class Accessors<T, TValue> : AccessorsBase
{
    public required Func<TValue?, T> Creator { get; init; }
    public required Func<T, TValue?> Extractor { get; init; }
}


// ChatGPT
file sealed class SingleValueWrapperConverter<T, TValue> : JsonConverter<T>
{
    private readonly Accessors<T, TValue> _accessor = Accessors.Get<T, TValue>();

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
        return _accessor.Creator(value);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, _accessor.Extractor(value), options);
    }
}

file sealed class SingleValueWrapperConverterFactory : JsonConverterFactory
{
    // Cache of created converters per wrapped type
    private static readonly ConcurrentDictionary<Type, JsonConverter> _converterCache = new();

    public override bool CanConvert(Type typeToConvert)
    {
        // Skip primitives and enums
        if (typeToConvert.IsPrimitive || typeToConvert.IsEnum)
        {
            return false;
        }
        if (_converterCache.ContainsKey(typeToConvert))
        {
            return true;
        }

        if (Accessors.TryAddAccessorsFor(typeToConvert) is { } accessor)
        {
            _ = accessor;
            return true;
        }

        return false;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (_converterCache.TryGetValue(typeToConvert, out var cached))
        {
            return cached;
        }

        var accessor = Accessors.Get(typeToConvert);
        var converterType = typeof(SingleValueWrapperConverter<,>).MakeGenericType(typeToConvert, accessor!.ValueType);
        var converter = (JsonConverter) Activator.CreateInstance(converterType)!;

        _converterCache[typeToConvert] = converter;
        return converter;
    }
}

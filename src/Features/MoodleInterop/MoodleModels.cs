using System.Xml.Serialization;

namespace QuizModels;

// ============================================================================
// XML Models (Immutable, for serialization)
// ============================================================================

[XmlRoot("quiz")]
public class Quiz
{
    [XmlElement("question")]
    public List<QuestionBase> Questions { get; set; } = new();
}

[XmlInclude(typeof(CategoryQuestion))]
[XmlInclude(typeof(EssayQuestion))]
[XmlInclude(typeof(MultiChoiceQuestion))]
public abstract class QuestionBase
{
    [XmlAttribute("type")]
    public string Type { get; set; } = string.Empty;
}

public class CategoryQuestion : QuestionBase
{
    [XmlElement("category")]
    public Category Category { get; set; } = new();

    [XmlElement("info")]
    public FormattedText Info { get; set; } = new();

    [XmlElement("idnumber")]
    public string IdNumber { get; set; } = string.Empty;
}

public class EssayQuestion : QuestionBase
{
    [XmlElement("name")]
    public TextElement Name { get; set; } = new();

    [XmlElement("questiontext")]
    public FormattedText QuestionText { get; set; } = new();

    [XmlElement("generalfeedback")]
    public FormattedText GeneralFeedback { get; set; } = new();

    [XmlElement("defaultgrade")]
    public decimal DefaultGrade { get; set; }

    [XmlElement("penalty")]
    public decimal Penalty { get; set; }

    [XmlElement("hidden")]
    public int Hidden { get; set; }

    [XmlElement("idnumber")]
    public string IdNumber { get; set; } = string.Empty;

    [XmlElement("responseformat")]
    public string ResponseFormat { get; set; } = string.Empty;

    [XmlElement("responserequired")]
    public int ResponseRequired { get; set; }

    [XmlElement("responsefieldlines")]
    public int ResponseFieldLines { get; set; }

    [XmlElement("minwordlimit")]
    public string MinWordLimit { get; set; } = string.Empty;

    [XmlElement("maxwordlimit")]
    public string MaxWordLimit { get; set; } = string.Empty;

    [XmlElement("attachments")]
    public int Attachments { get; set; }

    [XmlElement("attachmentsrequired")]
    public int AttachmentsRequired { get; set; }

    [XmlElement("maxbytes")]
    public int MaxBytes { get; set; }

    [XmlElement("filetypeslist")]
    public string FileTypesList { get; set; } = string.Empty;

    [XmlElement("graderinfo")]
    public FormattedText GraderInfo { get; set; } = new();

    [XmlElement("responsetemplate")]
    public FormattedText ResponseTemplate { get; set; } = new();
}

public class MultiChoiceQuestion : QuestionBase
{
    [XmlElement("name")]
    public TextElement Name { get; set; } = new();

    [XmlElement("questiontext")]
    public FormattedText QuestionText { get; set; } = new();

    [XmlElement("generalfeedback")]
    public FormattedText GeneralFeedback { get; set; } = new();

    [XmlElement("defaultgrade")]
    public decimal DefaultGrade { get; set; }

    [XmlElement("penalty")]
    public decimal Penalty { get; set; }

    [XmlElement("hidden")]
    public int Hidden { get; set; }

    [XmlElement("idnumber")]
    public string IdNumber { get; set; } = string.Empty;

    [XmlElement("single")]
    public bool Single { get; set; }

    [XmlElement("shuffleanswers")]
    public bool ShuffleAnswers { get; set; }

    [XmlElement("answernumbering")]
    public string AnswerNumbering { get; set; } = string.Empty;

    [XmlElement("showstandardinstruction")]
    public int ShowStandardInstruction { get; set; }

    [XmlElement("correctfeedback")]
    public FormattedText CorrectFeedback { get; set; } = new();

    [XmlElement("partiallycorrectfeedback")]
    public FormattedText PartiallyCorrectFeedback { get; set; } = new();

    [XmlElement("incorrectfeedback")]
    public FormattedText IncorrectFeedback { get; set; } = new();

    [XmlElement("shownumcorrect")]
    public ShownumCorrect? ShowNumCorrect { get; set; }

    [XmlElement("answer")]
    public List<Answer> Answers { get; set; } = new();
}

public class Category
{
    [XmlElement("text")]
    public string Text { get; set; } = string.Empty;
}

public class TextElement
{
    [XmlElement("text")]
    public string Text { get; set; } = string.Empty;
}

public class FormattedText
{
    [XmlAttribute("format")]
    public string Format { get; set; } = string.Empty;

    [XmlElement("text")]
    public string Text { get; set; } = string.Empty;
}

public class Answer
{
    [XmlAttribute("fraction")]
    public decimal Fraction { get; set; }

    [XmlAttribute("format")]
    public string Format { get; set; } = string.Empty;

    [XmlElement("text")]
    public string Text { get; set; } = string.Empty;

    [XmlElement("feedback")]
    public FormattedText Feedback { get; set; } = new();
}

public class ShownumCorrect
{
}

// ============================================================================
// Mutable Builder Models
// ============================================================================

public class MutableEssayQuestion
{
    public string Name { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public decimal DefaultGrade { get; set; } = 1.0m;
}

public class MutableMultiChoiceQuestion
{
    public string Name { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public decimal DefaultGrade { get; set; } = 1.0m;
    public List<MutableAnswer> Answers { get; set; } = new();
}

public class MutableAnswer
{
    public string Text { get; set; } = string.Empty;
    public decimal Fraction { get; set; }
}

public class AnswerBuilder
{
    private string _text = string.Empty;
    private decimal _fraction = 0m;

    public AnswerBuilder Text(string text)
    {
        _text = text;
        return this;
    }

    public AnswerBuilder Code(string code)
    {
        _text = $"<pre>{code}</pre>";
        return this;
    }

    public AnswerBuilder Fraction(decimal fraction)
    {
        _fraction = fraction;
        return this;
    }

    internal MutableAnswer Build()
    {
        return new MutableAnswer
        {
            Text = _text,
            Fraction = _fraction,
        };
    }
}

// ============================================================================
// Builders
// ============================================================================

public class QuizBuilder
{
    private readonly List<object> _questionBuilders = new();
    private string _categoryText = "$module$/top/Implicit pentru Atestare 1";
    private string _categoryInfo = "Categoria implicită pentru întrebările partajate în contextul 'Atestare 1'.";

    public QuizBuilder CategoryText(string text)
    {
        _categoryText = text;
        return this;
    }

    public QuizBuilder CategoryInfo(string info)
    {
        _categoryInfo = info;
        return this;
    }

    public QuizBuilder Essay(Action<EssayQuestionBuilder> configure)
    {
        var builder = new EssayQuestionBuilder();
        configure(builder);
        _questionBuilders.Add(builder);
        return this;
    }

    public QuizBuilder MultiChoice(Action<MultiChoiceQuestionBuilder> configure)
    {
        var builder = new MultiChoiceQuestionBuilder();
        configure(builder);
        _questionBuilders.Add(builder);
        return this;
    }

    public Quiz Build()
    {
        var questions = new List<QuestionBase>();

        // Add category question first
        questions.Add(new CategoryQuestion
        {
            Type = "category",
            Category = new Category { Text = _categoryText },
            Info = new FormattedText
            {
                Format = "moodle_auto_format",
                Text = _categoryInfo,
            },
            IdNumber = string.Empty,
        });

        // Build all stored question builders
        foreach (var builder in _questionBuilders)
        {
            if (builder is EssayQuestionBuilder essayBuilder)
            {
                questions.Add(essayBuilder.Build());
            }
            else if (builder is MultiChoiceQuestionBuilder multiChoiceBuilder)
            {
                questions.Add(multiChoiceBuilder.Build());
            }
        }

        return new Quiz { Questions = questions };
    }
}

public class EssayQuestionBuilder
{
    private readonly MutableEssayQuestion _question = new();

    public EssayQuestionBuilder Name(string name)
    {
        _question.Name = name;
        return this;
    }

    public EssayQuestionBuilder QuestionText(string text)
    {
        _question.QuestionText = text;
        return this;
    }

    public EssayQuestionBuilder DefaultGrade(decimal grade)
    {
        _question.DefaultGrade = grade;
        return this;
    }

    internal EssayQuestion Build()
    {
        return new EssayQuestion
        {
            Type = "essay",
            Name = new TextElement { Text = _question.Name },
            QuestionText = new FormattedText
            {
                Format = "html",
                Text = $"<p>{_question.QuestionText}</p>",
            },
            GeneralFeedback = new FormattedText { Format = "html", Text = string.Empty },
            DefaultGrade = _question.DefaultGrade,
            Penalty = 0.0m,
            Hidden = 0,
            IdNumber = string.Empty,
            ResponseFormat = "monospaced",
            ResponseRequired = 1,
            ResponseFieldLines = 40,
            MinWordLimit = string.Empty,
            MaxWordLimit = string.Empty,
            Attachments = 0,
            AttachmentsRequired = 0,
            MaxBytes = 0,
            FileTypesList = string.Empty,
            GraderInfo = new FormattedText { Format = "html", Text = string.Empty },
            ResponseTemplate = new FormattedText { Format = "html", Text = string.Empty },
        };
    }
}

public class MultiChoiceQuestionBuilder
{
    private readonly MutableMultiChoiceQuestion _question = new();

    public MultiChoiceQuestionBuilder Name(string name)
    {
        _question.Name = name;
        return this;
    }

    public MultiChoiceQuestionBuilder QuestionText(string text)
    {
        _question.QuestionText = text;
        return this;
    }

    public MultiChoiceQuestionBuilder DefaultGrade(decimal grade)
    {
        _question.DefaultGrade = grade;
        return this;
    }

    public MultiChoiceQuestionBuilder AddAnswer(string text, decimal fraction)
    {
        _question.Answers.Add(new MutableAnswer
        {
            Text = text,
            Fraction = fraction,
        });
        return this;
    }

    public MultiChoiceQuestionBuilder Answer(Action<AnswerBuilder> configure)
    {
        var builder = new AnswerBuilder();
        configure(builder);
        _question.Answers.Add(builder.Build());
        return this;
    }

    internal MultiChoiceQuestion Build()
    {
        // Separate positive and negative fractions
        var positiveFractions = _question.Answers.Where(a => a.Fraction > 0);
        // var negativeFractions = _question.Answers.Where(a => a.Fraction < 0).ToList();
        // var zeroFractions = _question.Answers.Where(a => a.Fraction == 0).ToList();

        // Calculate scaling factor for positive fractions to sum to 100
        var totalPositive = positiveFractions.Sum(a => a.Fraction);
        var scale = totalPositive > 0 ? 100m / totalPositive : 1m;

        // Helper function to round to standard percentages
        decimal RoundToStandardPercentage(decimal value)
        {
            var absValue = Math.Abs(value);
            var sign = value >= 0 ? 1 : -1;

            // Define standard percentage values
            var standards = new[]
            {
                100m, 90m, 83.3333333m, 80m, 75m, 70m, 66.6666667m, 60m, 50m, 40m,
                33.3333333m, 30m, 25m, 20m, 16.6666667m, 14.285714m, 12.5m, 11.1111111m, 10m,
            };

            // Find closest standard percentage
            var closest = standards.OrderBy(s => Math.Abs(s - absValue)).First();
            return closest * sign;
        }

        var normalizedAnswers = new List<Answer>();

        foreach (var answer in _question.Answers)
        {
            decimal fraction;
            if (answer.Fraction > 0)
            {
                // Scale positive fractions to sum to 100
                fraction = RoundToStandardPercentage(answer.Fraction * scale);
            }
            else if (answer.Fraction < 0)
            {
                // Scale negative fractions by the same factor, capped at -100
                var scaledNegative = answer.Fraction * scale;
                fraction = Math.Max(-100, RoundToStandardPercentage(scaledNegative));
            }
            else
            {
                fraction = 0;
            }

            normalizedAnswers.Add(new Answer
            {
                Fraction = fraction,
                Format = "html",
                Text = answer.Text,
                Feedback = new FormattedText { Format = "html", Text = string.Empty },
            });
        }

        return new MultiChoiceQuestion
        {
            Type = "multichoice",
            Name = new TextElement { Text = _question.Name },
            QuestionText = new FormattedText
            {
                Format = "html",
                Text = $"<p>{_question.QuestionText}</p>",
            },
            GeneralFeedback = new FormattedText { Format = "html", Text = string.Empty },
            DefaultGrade = _question.DefaultGrade,
            Penalty = 0.3333333m,
            Hidden = 0,
            IdNumber = string.Empty,
            Single = false,
            ShuffleAnswers = false,
            AnswerNumbering = "abc",
            ShowStandardInstruction = 1,
            CorrectFeedback = new FormattedText
            {
                Format = "html",
                Text = "<p>Răspunsul dumneavoastră este corect.</p>",
            },
            PartiallyCorrectFeedback = new FormattedText
            {
                Format = "html",
                Text = "<p>Răspunsul dumneavoastră este parțial corect.</p>",
            },
            IncorrectFeedback = new FormattedText
            {
                Format = "html",
                Text = "<p>Răspunsul dumneavoastră este incorect.</p>",
            },
            ShowNumCorrect = new ShownumCorrect(),
            Answers = normalizedAnswers,
        };
    }
}

// ============================================================================
// Example Usage
// ============================================================================

public class Example
{
    public static void Demo()
    {
        var quiz = new QuizBuilder()
            .CategoryText("$module$/top/Implicit pentru Atestare 1")
            .CategoryInfo("Categoria implicită pentru întrebările partajate în contextul 'Atestare 1'.")
            .Essay(q => q
                .Name("encapsulation_1")
                .QuestionText("explain some stuff")
                .DefaultGrade(1.0m))
            .MultiChoice(q => q
                .Name("magic_1")
                .QuestionText("Что из следующего является примером использования одного или нескольких magic значений?")
                .DefaultGrade(1.0m)
                .Answer(a => a.Code("var v = get_value(1, 2, true);").Fraction(1.0m))
                .Answer(a => a.Code("var age = 1;\nvar child_count = 2;\nvar has_parents = false;\nvar v = get_value(age, child_count, has_parents);").Fraction(0.0m))
                .Answer(a => a.Code("var age = 1;\nvar child_count = 2;\nvar v = get_value(age, child_count, false);").Fraction(1.0m))
                .Answer(a => a.Code("var v = get_value(age: 1, child_count: 2, has_parents: true);").Fraction(0.0m)))
            .Build();

        // Serialize to XML
        var serializer = new XmlSerializer(typeof(Quiz));
        using var writer = new StringWriter();
        serializer.Serialize(writer, quiz);
        Console.WriteLine(writer.ToString());
    }
}

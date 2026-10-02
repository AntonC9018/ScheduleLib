using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing.Moodle;

public sealed class ParsedMoodlePath
{
    public required string Base { get; init; }
    public required Faculty Faculty { get; init; }
    public required QualificationType QualificationType { get; init; }
    public required Specialty Specialty { get; init; }
    public required Grade Grade { get; init; }
    public required string CourseName { get; init; }
    public required string SectionName { get; init; }
    public required int TestNumber { get; init; }
}

// AI generated, all of the below.
// TODO: Do this well.

// Tokenizer for Moodle paths
public static class MoodlePathTokenType
{
    public const TokenType Text = TokenType.Invalid + 1;
    public const TokenType Ciclul = Text + 1;
    public const TokenType Anul = Ciclul + 1;
    public const TokenType Atestare = Anul + 1;
    public const TokenType Number = Atestare + 1;
    public const TokenType RomanNumeral = Number + 1;
}

public sealed class MoodlePathTokenReader : ITokenReader
{
    public static readonly MoodlePathTokenReader Instance = new();
    public TokenTypeLabels Labels { get; } = LexerHelper.CreateLabels(typeof(MoodlePathTokenType));

    public TokenType Read(ref SequenceReader reader)
    {
        // Skip whitespace
        if (reader.SkipWhitespace().SkippedAny)
        {
            return TokenType.Whitespace;
        }

        // Check for keywords
        if (reader.ConsumeExactString("Ciclul", StringComparison.OrdinalIgnoreCase))
        {
            return MoodlePathTokenType.Ciclul;
        }

        if (reader.ConsumeExactString("Anul", StringComparison.OrdinalIgnoreCase))
        {
            return MoodlePathTokenType.Anul;
        }

        if (reader.ConsumeExactString("Atestare", StringComparison.OrdinalIgnoreCase))
        {
            return MoodlePathTokenType.Atestare;
        }

        // Check for numbers (Arabic numerals)
        var numResult = reader.ConsumePositiveIntWithMaxLength(3);
        if (numResult.HasValue)
        {
            return MoodlePathTokenType.Number;
        }

        // Check for Roman numerals
        var romanResult = reader.ReadRoman();
        if (romanResult.Status == ReadRomanStatus.Ok)
        {
            return MoodlePathTokenType.RomanNumeral;
        }

        // Read any other text (words, special characters, etc.)
        if (!reader.IsEmpty)
        {
            reader.Skip(new SkipTextImpl());
            return MoodlePathTokenType.Text;
        }

        return TokenType.Invalid;
    }

    private struct SkipTextImpl : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            // Stop at whitespace
            if (char.IsWhiteSpace(ch))
            {
                return false;
            }

            return true;
        }
    }
}

public static class MoodlePathParser
{
    public static ParsedMoodlePath? TryParse(IEnumerable<string> pathSegments)
    {
        var segments = pathSegments.ToList();

        // Expected pattern:
        // 0: Base (e.g., "Cursuri USM")
        // 1: Faculty (e.g., "Matematică și Informatică")
        // 2: Ciclul X QualificationType (e.g., "Ciclul I Licență")
        // 3: Specialty (e.g., "Informatică aplicată")
        // 4: Anul Grade (e.g., "Anul III")
        // 5: CourseName (e.g., "Modele de design software")
        // 6: SectionName (e.g., "Atestare 1")
        // 7: Rezultate or Note

        if (segments.Count < 7)
        {
            return null;
        }

        var baseSegment = segments[0];
        var faculty = new Faculty(segments[1]);

        // Parse "Ciclul X QualificationType"
        var qualificationSegment = segments[2];
        var (cycle, qualificationType) = ParseQualificationSegment(qualificationSegment);
        _ = cycle;

        if (qualificationType == null)
        {
            return null;
        }

        var specialty = new Specialty(segments[3]);

        // Parse "Anul Grade"
        var gradeSegment = segments[4];
        var grade = ParseGradeSegment(gradeSegment);
        var courseName = segments[5];

        // Parse "Atestare TestNumber" or just section name
        var sectionSegment = segments[6];
        var (sectionName, testNumber) = ParseSectionSegment(sectionSegment);

        return new ParsedMoodlePath
        {
            Base = baseSegment,
            Faculty = faculty,
            QualificationType = qualificationType.Value,
            Specialty = specialty,
            Grade = grade,
            CourseName = courseName,
            SectionName = sectionName,
            TestNumber = testNumber,
        };
    }

    private static (int? cycle, QualificationType? qualificationType) ParseQualificationSegment(string segment)
    {
        var lexer = new Lexer(MoodlePathTokenReader.Instance);
        using var e = SingleItemEnumerator.Create(segment.AsMemory());
        lexer.Reset(e);

        var scope = lexer.Scope();

        // Expect: Ciclul <number> <text>
        if (!scope.TryConsume(MoodlePathTokenType.Ciclul))
        {
            return (null, null);
        }

        scope.ConsumeAllConsecutive([TokenType.Whitespace]);

        if (!scope.CanPeek(1))
        {
            return (null, null);
        }

        int? cycleNumber = null;
        var token = scope.Current;
        if (token.Type == MoodlePathTokenType.RomanNumeral)
        {
            cycleNumber = NumberHelper.FromRoman(token.Value.Span);
            scope.Move();
        }
        else if (token.Type == MoodlePathTokenType.Number)
        {
            if (int.TryParse(token.Value.Span, out int num))
            {
                cycleNumber = num;
            }
            scope.Move();
        }

        if (cycleNumber == null)
        {
            return (null, null);
        }

        scope.ConsumeAllConsecutive([TokenType.Whitespace]);

        // Read qualification type text
        var qualificationText = "";
        while (scope.CanPeek(1))
        {
            var t = scope.Current;
            if (t.Type == TokenType.Whitespace)
            {
                qualificationText += " ";
            }
            else
            {
                qualificationText += t.Value.ToString();
            }
            scope.Move();
        }

        qualificationText = qualificationText.Trim();
        var qualType = ParseQualificationType(qualificationText);

        return (cycleNumber, qualType);
    }

    private static Grade ParseGradeSegment(string segment)
    {
        var lexer = new Lexer(MoodlePathTokenReader.Instance);
        using var e = SingleItemEnumerator.Create(segment.AsMemory());
        lexer.Reset(e);

        var scope = lexer.Scope();

        // Expect: Anul <roman/number>
        if (!scope.TryConsume(MoodlePathTokenType.Anul))
        {
            return Grade.Invalid;
        }

        scope.ConsumeAllConsecutive([TokenType.Whitespace]);

        if (!scope.CanPeek(1))
        {
            return Grade.Invalid;
        }

        int? gradeNumber = null;
        var token = scope.Current;
        if (token.Type == MoodlePathTokenType.RomanNumeral)
        {
            gradeNumber = NumberHelper.FromRoman(token.Value.Span);
        }
        else if (token.Type == MoodlePathTokenType.Number)
        {
            if (int.TryParse(token.Value.Span, out int num))
            {
                gradeNumber = num;
            }
        }

        if (gradeNumber == null)
        {
            return Grade.Invalid;
        }

        return new((int) gradeNumber);
    }

    private static (string sectionName, int testNumber) ParseSectionSegment(string segment)
    {
        var lexer = new Lexer(MoodlePathTokenReader.Instance);
        using var e = SingleItemEnumerator.Create(segment.AsMemory());
        lexer.Reset(e);

        var scope = lexer.Scope();

        // Check if starts with "Atestare"
        if (scope.TryConsume(MoodlePathTokenType.Atestare))
        {
            scope.ConsumeAllConsecutive([TokenType.Whitespace]);

            int testNumber = 0;
            if (scope.CanPeek(1))
            {
                var token = scope.Current;
                if (token.Type == MoodlePathTokenType.Number)
                {
                    int.TryParse(token.Value.Span, out testNumber);
                }
            }

            return (segment, testNumber);
        }

        // Otherwise just return the segment as-is
        return (segment, 0);
    }

    private static QualificationType? ParseQualificationType(string text)
    {
        if (text.AsSpan().StartsWith("Licen", StringComparison.OrdinalIgnoreCase))
        {
            return QualificationType.Licenta;
        }
        if (text.AsSpan().StartsWith("Master", StringComparison.OrdinalIgnoreCase))
        {
            return QualificationType.Master;
        }
        // Add more as needed
        return null;
    }
}

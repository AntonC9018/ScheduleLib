using System.Globalization;
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

public static class MoodlePathParser
{
    // Expected breadcrumb layout:
    // 0: Base (e.g., "Cursuri USM")
    // 1: Faculty (e.g., "Matematică și Informatică")
    // 2: "Ciclul <roman|int> <qualification>" (e.g., "Ciclul I Licență")
    // 3: Specialty (e.g., "Informatică aplicată")
    // 4: "Anul <roman|int>" (e.g., "Anul III")
    // 5: CourseName (e.g., "Modele de design software")
    // 6: SectionName (e.g., "Atestare 1")
    private const int MinimumSegmentCount = 7;

    public static ParsedMoodlePath? TryParse(IEnumerable<string> pathSegments)
    {
        var segments = pathSegments as IReadOnlyList<string> ?? [.. pathSegments];
        if (segments.Count < MinimumSegmentCount)
        {
            return null;
        }

        if (ParseQualificationSegment(segments[2]) is not { } qualificationType)
        {
            return null;
        }

        var grade = ParseGradeSegment(segments[4]);
        var testNumber = ParseTestNumber(segments[6]);

        return new ParsedMoodlePath
        {
            Base = segments[0],
            Faculty = new Faculty(segments[1]),
            QualificationType = qualificationType,
            Specialty = new Specialty(segments[3]),
            Grade = grade,
            CourseName = segments[5],
            SectionName = segments[6],
            TestNumber = testNumber,
        };
    }

    private static QualificationType? ParseQualificationSegment(string segment)
    {
        var reader = new SequenceReader(segment);
        reader.SkipWhitespace();
        if (!ConsumeKeyword(ref reader, "Ciclul"))
        {
            return null;
        }
        if (ConsumePositiveIntOrRoman(ref reader) is null)
        {
            return null;
        }
        if (!reader.SkipWhitespace().SkippedAny)
        {
            return null;
        }

        var remaining = reader.PeekSpanUntilEnd().Trim();
        var qualificationType = ParseQualificationType(remaining);
        if (qualificationType is not null)
        {
            return qualificationType;
        }
        return null;
    }

    private static Grade ParseGradeSegment(string segment)
    {
        var reader = new SequenceReader(segment);
        reader.SkipWhitespace();
        if (!ConsumeKeyword(ref reader, "Anul"))
        {
            return Grade.Invalid;
        }
        if (ConsumePositiveIntOrRoman(ref reader) is not { } gradeNumber)
        {
            return Grade.Invalid;
        }

        reader.SkipWhitespace();
        if (!reader.IsEmpty)
        {
            return Grade.Invalid;
        }

        return new Grade(gradeNumber);
    }

    private static int ParseTestNumber(string segment)
    {
        var reader = new SequenceReader(segment);
        reader.SkipWhitespace();
        if (!ConsumeKeyword(ref reader, "Atestare"))
        {
            return 0;
        }
        if (ConsumePositiveInt(ref reader) is not { } testNumber)
        {
            return 0;
        }

        return testNumber;
    }

    private static bool ConsumeKeyword(ref SequenceReader reader, string keyword)
    {
        if (!reader.ConsumeExactString(keyword, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return reader.SkipWhitespace().SkippedAny;
    }

    private static int? ConsumePositiveIntOrRoman(ref SequenceReader reader)
    {
        if (ConsumePositiveInt(ref reader) is { } number)
        {
            return number;
        }

        var buffered = reader.BufferedView();
        var romanResult = buffered.ReadRoman();
        if (romanResult.Status != ReadRomanStatus.Ok)
        {
            return null;
        }
        if (!buffered.IsEmpty && !char.IsWhiteSpace(buffered.Current))
        {
            return null;
        }

        var romanSpan = reader.PeekSpanUntilPosition(buffered.Position);
        reader.MoveTo(buffered.Position);
        var romanValue = NumberHelper.FromRoman(romanSpan);
        if (romanValue is not null)
        {
            return romanValue;
        }
        return null;
    }

    private static int? ConsumePositiveInt(ref SequenceReader reader)
    {
        var buffered = reader.BufferedView();
        if (!buffered.SkipNumbers().SkippedAny)
        {
            return null;
        }
        if (!buffered.IsEmpty && !char.IsWhiteSpace(buffered.Current))
        {
            return null;
        }

        var digits = reader.PeekSpanUntilPosition(buffered.Position);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            return null;
        }
        if (value <= 0)
        {
            return null;
        }

        reader.MoveTo(buffered.Position);
        return value;
    }

    private static QualificationType? ParseQualificationType(ReadOnlySpan<char> text)
    {
        if (text.StartsWith("Licen", StringComparison.OrdinalIgnoreCase))
        {
            return QualificationType.Licenta;
        }
        if (text.StartsWith("Master", StringComparison.OrdinalIgnoreCase))
        {
            return QualificationType.Master;
        }
        return null;
    }
}

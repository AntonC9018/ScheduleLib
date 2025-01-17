using System.Diagnostics;
using System.Runtime.InteropServices;
using App.Generation;

namespace App.Parsing;

public struct ParseLessonsContext
{
    public required LessonTypeParser LessonTypeParser;
    public required ParityParser ParityParser;
    public required IEnumerable<string> Lines;
}

public ref struct ParsedLesson()
{
    public required StringSegment LessonName;
    public required StringSegment TeacherName;
    public TimeOnly? StartTime = null;
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public SubGroupNumber SubGroupNumber = SubGroupNumber.All;
}

public struct LessonInParsing()
{
    public StringSegment LessonName;
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public SubGroupNumber SubGroupNumber = SubGroupNumber.All;
}

file enum ParsingStep
{
    TimeOverride,
    LessonName,
    OptionalParens,
    OptionalSubGroupOrRepeatLessonName,
    TeacherName,
    CabName,
}


public struct StringSegment
{
    public required string Source;
    public required int Start;
    public required int EndInclusive;
    public ReadOnlySpan<char> Span => Source.AsSpan(Start, EndInclusive - Start);
}

file struct ParsingState()
{
    public ParsingStep Step = ParsingStep.TimeOverride;
    public TimeOnly? StartTime = null;
    public List<LessonInParsing> LessonsInParsing = new();
    public bool SeenOneLesson = false;

    public ref LessonInParsing CurrentLesson => ref CollectionsMarshal.AsSpan(LessonsInParsing)[^1];

    public void ResetLesson()
    {
        LessonsInParsing.Clear();
        StartTime = null;
    }
}

public sealed class WrongFormatException : Exception
{
}

public static class ParsingHelper
{
    public static IEnumerable<ParsedLesson> ParseLessons(ParseLessonsContext context)
    {
        ParsingState state = new();
        var lessonNames = new List<StringSegment>();

        foreach (var line in context.Lines)
        {
            var parser = new Parser(line);

            while (true)
            {
                parser.SkipWhitespace();
                if (parser.IsEmpty)
                {
                    break;
                }

                switch (state.Step)
                {
                    case ParsingStep.TimeOverride:
                    {
                        var bparser = parser.BufferedView();
                        if (ParseTime(ref bparser) is { } time)
                        {
                            parser.MoveTo(bparser.Position);
                            state.StartTime = time;
                        }
                        else if (bparser.Position != parser.Position)
                        {
                            throw new WrongFormatException();
                        }

                        state.Step = ParsingStep.LessonName;
                        break;
                    }
                    case ParsingStep.LessonName:
                    {
                        // Until end of line or paren
                        var bparser = parser.BufferedView();
                        bool skipped = bparser.Skip(new ParenSkip());
                        if (!skipped)
                        {
                            throw new WrongFormatException();
                        }

                        var name = SourceUntilExclusive(parser, bparser);
                        state.LessonsInParsing.Add(new()
                        {
                            LessonName = name,
                        });
                        parser.MoveTo(bparser.Position);
                        state.Step = ParsingStep.OptionalParens;
                        break;
                    }
                    case ParsingStep.OptionalParens:
                    {
                        if (parser.Current != '(')
                        {
                            state.Step = ParsingStep.OptionalSubGroupOrRepeatLessonName;
                            break;
                        }

                        parser.Move();

                        var bparser = parser.BufferedView();
                        SkipParenListItem(ref bparser);

                        var a = parser.PeekSpanUntilPosition(bparser.Position);
                        parser.MoveTo(bparser.Position);

                        ReadOnlySpan<char> b = default;
                        if (bparser.Current == ',')
                        {
                            SkipParenListItem(ref bparser);
                            if (bparser.Current == ',')
                            {
                                throw new WrongFormatException();
                            }

                            Debug.Assert(bparser.Current == ')');
                            b = parser.PeekSpanUntilPosition(bparser.Position);

                            bparser.Move();
                            parser.MoveTo(bparser.Position);
                        }

                        Process(a, b);
                        state.Step = ParsingStep.OptionalSubGroupOrRepeatLessonName;
                        break;

                        void Process(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
                        {
                            LessonType? lessonType = null;
                            Parity? parity = null;

                            ProcessOne(a);
#pragma warning disable CA2265 // Comparing span to default
                            if (b != default)
#pragma warning restore CA2265
                            {
                                ProcessOne(b);
                            }

                            if (lessonType is { } lessonType2)
                            {
                                state.CurrentLesson.LessonType = lessonType2;
                            }
                            if (parity is { } parity2)
                            {
                                state.CurrentLesson.Parity = parity2;
                            }

                            void ProcessOne(ReadOnlySpan<char> it)
                            {
                                if (context.LessonTypeParser.Parse(it) is { } lessonType1)
                                {
                                    if (lessonType is not null)
                                    {
                                        throw new WrongFormatException();
                                    }
                                    lessonType = lessonType1;
                                    return;
                                }

                                if (context.ParityParser.Parse(it) is { } parity1)
                                {
                                    if (parity is not null)
                                    {
                                        throw new WrongFormatException();
                                    }
                                    parity = parity1;
                                    return;
                                }

                                throw new WrongFormatException();
                            }
                        }
                    }
                    case ParsingStep.OptionalSubGroupOrRepeatLessonName:
                    {
                        break;
                    }
                }
            }
        }
    }

    public static StringSegment SourceUntilExclusive(Parser a, Parser b)
    {
        Debug.Assert(ReferenceEquals(a.Source, b.Source));

        int start = a.Position;
        int end = b.Position;
        return new()
        {
            Source = a.Source,
            Start = start,
            EndInclusive = end - 1,
        };
    }

    private static void SkipParenListItem(ref Parser p)
    {
        bool skipped = p.Skip(new SkipUntil([
            ',',
            ')',
        ]));
        if (!skipped)
        {
            // expected non-empty parens
            throw new WrongFormatException();
        }
        if (p.IsEmpty)
        {
            throw new WrongFormatException();
        }
    }

    private ref struct SkipUntil : IShouldSkip
    {
        private readonly ReadOnlySpan<char> _chars;
        public SkipUntil(ReadOnlySpan<char> chars) => _chars = chars;
        public bool ShouldSkip(char ch) => !_chars.Contains(ch);
    }

    private struct ParenSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => ch != ')';
    }

    private struct NumberSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => char.IsNumber(ch);
    }
    public static bool SkipNumbers(this ref Parser parser)
    {
        return parser.Skip(new NumberSkip());
    }

    public static TimeOnly? ParseTime(ref Parser parser)
    {
        var bparser = parser.BufferedView();
        if (!parser.SkipNumbers())
        {
            return null;
        }

        uint hours;
        {
            var numberSpan = parser.PeekSpanUntilPosition(bparser.Position);
            if (!uint.TryParse(numberSpan, out hours))
            {
                return null;
            }

            parser.MoveTo(bparser.Position);
        }

        {
            if (parser.Current != ':')
            {
                return null;
            }
            parser.Move();
        }

        uint minutes;
        {
            var result = parser.ConsumePositiveInt(length: 2);
            if (result.Status != ConsumeIntStatus.Ok)
            {
                return null;
            }

            minutes = result.Value;
        }

        {
            var timeSpan = new TimeSpan(
                hours: (int) hours,
                minutes: (int) minutes,
                seconds: 0);
            var ret = TimeOnly.FromTimeSpan(timeSpan);
            return ret;
        }
    }
}



public sealed class LessonTypeParser
{
    public LessonType? Parse(ReadOnlySpan<char> type)
    {
        var names = LessonTypeConstants.Names;
        for (int i = 0; i < names.Length; i++)
        {
            var n = names[i].AsSpan();
            if (n.Equals(type, StringComparison.OrdinalIgnoreCase))
            {
                return (LessonType) i;
            }
        }
        return null;
    }
}

public sealed class ParityParser
{
    public Parity? Parse(ReadOnlySpan<char> parity)
    {
        bool Equals(ReadOnlySpan<char> a, string b)
        {
            return a.Equals(b.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }
        if (Equals(parity, "par"))
        {
            return Parity.EvenWeek;
        }
        if (Equals(parity, "impar"))
        {
            return Parity.OddWeek;
        }
        return null;
    }
}

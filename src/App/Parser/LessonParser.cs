using System.Diagnostics;
using System.Runtime.InteropServices;
using App.Generation;

namespace App.Parsing.Lesson;

public struct ParseLessonsParams
{
    public required LessonTypeParser LessonTypeParser;
    public required ParityParser ParityParser;
    public required IEnumerable<string> Lines;
}

public struct ParsedLesson()
{
    public required StringSegment LessonName;
    public required StringSegment TeacherName;
    public required StringSegment RoomName;
    public TimeOnly? StartTime = null;
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public SubGroupNumber SubGroupNumber = SubGroupNumber.All;
}

public struct SubLessonInParsing()
{
    public StringSegment LessonName = default;
    public SubGroupNumber SubGroupNumber = SubGroupNumber.All;
    public List<Modifiers> AllModifiers = new();

    public ref Modifiers Modifiers(SubGroupNumber subGroupNumber = default)
    {
        if (subGroupNumber == default)
        {
            subGroupNumber = SubGroupNumber.All;
        }

        {
            var mods = CollectionsMarshal.AsSpan(AllModifiers);
            for (int i = 0; i < mods.Length; i++)
            {
                ref var it = ref mods[i];
                if (it.SubGroupNumber == subGroupNumber)
                {
                    return ref it;
                }
            }
        }

        {
            AllModifiers.Add(new()
            {
                SubGroupNumber = subGroupNumber,
            });
            return ref CollectionsMarshal.AsSpan(AllModifiers)[^1];
        }
    }
}

public struct Modifiers()
{
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public SubGroupNumber SubGroupNumber = SubGroupNumber.All;
}

public struct CommonLessonInParsing()
{
    public TimeOnly? StartTime = null;
    public StringSegment TeacherName;
    public StringSegment RoomName;
}

public enum ParsingStep
{
    Start,
    TimeOverride,
    LessonName,
    OptionalParens,
    OptionalSubGroup,
    TeacherName,
    RoomName,
    Output,
}


public struct StringSegment
{
    public required string Source;
    public required int Start;
    public required int EndInclusive;
    public readonly ReadOnlySpan<char> Span => Source.AsSpan(Start, EndInclusive - Start + 1);
    public readonly override string ToString() => Span.ToString();

    public readonly StringSegment TrimEnd()
    {
        int end = EndInclusive;
        while (true)
        {
            if (!char.IsWhiteSpace(Source[end]))
            {
                break;
            }
            end--;

            if (end < Start)
            {
                break;
            }
        }
        return this with
        {
            EndInclusive = end,
        };
    }
}

public struct ParsingState()
{
    public ParsingStep Step = ParsingStep.Start;
    public CommonLessonInParsing CommonLesson = new();
    public List<SubLessonInParsing> LessonsInParsing = new();

    public ref SubLessonInParsing CurrentSubLesson => ref CollectionsMarshal.AsSpan(LessonsInParsing)[^1];

    public void Reset()
    {
        Step = ParsingStep.TimeOverride;
        LessonsInParsing.Clear();
        CommonLesson = new();
    }
}

public sealed class WrongFormatException : Exception
{
}

public ref struct ParsingContext
{
    public required ref ParseLessonsParams Params;
    public required ref ParsingState State;
    public required ref Parser Parser;
}

public static class LessonParsingHelper
{
    public static IEnumerable<ParsedLesson> ParseLessons(ParseLessonsParams p)
    {
        ParsingState state = new();

        foreach (var line in p.Lines)
        {
            var parser = new Parser(line);

            while (true)
            {
                parser.SkipWhitespace();
                if (parser.IsEmpty)
                {
                    break;
                }

                if (state.Step == ParsingStep.Output)
                {
                    state.Reset();
                }

                DoParsingIter(new()
                {
                    Params = ref p,
                    Parser = ref parser,
                    State = ref state,
                });
                if (state.Step != ParsingStep.Output)
                {
                    continue;
                }

                foreach (var lesson in state.LessonsInParsing)
                {
                    foreach (var modifiers in lesson.AllModifiers)
                    {
                        yield return new()
                        {
                            LessonName = lesson.LessonName,
                            TeacherName = state.CommonLesson.TeacherName,
                            RoomName = state.CommonLesson.RoomName,
                            Parity = modifiers.Parity,
                            LessonType = modifiers.LessonType,
                            SubGroupNumber = modifiers.SubGroupNumber != SubGroupNumber.All
                                ? modifiers.SubGroupNumber
                                : lesson.SubGroupNumber,
                            StartTime = state.CommonLesson.StartTime,
                        };
                    }
                }
            }
        }

        if (state.Step != ParsingStep.Output
            && state.Step != ParsingStep.Start)
        {
            throw new WrongFormatException();
        }
    }

    private static void DoParsingIter(ParsingContext c)
    {
        switch (c.State.Step)
        {
            case ParsingStep.TimeOverride:
            case ParsingStep.Start:
            {
                var bparser = c.Parser.BufferedView();
                if (ParserHelper.ParseTime(ref bparser) is { } time)
                {
                    c.Parser.MoveTo(bparser.Position);
                    c.State.CommonLesson.StartTime = time;
                }
                else if (bparser.Position != c.Parser.Position)
                {
                    throw new WrongFormatException();
                }

                c.State.Step = ParsingStep.LessonName;
                break;
            }
            case ParsingStep.LessonName:
            {
                // Until end of line or paren
                var bparser = c.Parser.BufferedView();
                bparser.Skip(new SkipUntil(['(', ',']));

                var name = SourceUntilExclusive(c.Parser, bparser);
                c.State.LessonsInParsing.Add(new()
                {
                    LessonName = name,
                });
                c.Parser.MoveTo(bparser.Position);
                c.State.Step = ParsingStep.OptionalParens;
                break;
            }
            case ParsingStep.OptionalParens:
            {
                if (c.Parser.Current != '(')
                {
                    c.State.Step = ParsingStep.OptionalSubGroup;
                    break;
                }

                c.Parser.Move();

                while (true)
                {
                    var bparser = c.Parser.BufferedView();
                    SkipParenListItem(ref bparser);
                    var it = c.Parser.PeekSpanUntilPosition(bparser.Position);
                    Process(c, it);
                    c.Parser.MoveTo(bparser.Position);

                    bool isEnd = bparser.Current == ')';
                    c.Parser.Move();

                    if (isEnd)
                    {
                        break;
                    }
                }

                c.State.Step = ParsingStep.OptionalSubGroup;
                break;

                static void Process(ParsingContext c, ReadOnlySpan<char> it)
                {
                    // Check if it's the subgroup form.
                    // ROMAN-modifier
                    var subgroup = ParseOutSubGroup(ref it);
                    ref var modifiers = ref c.State.CurrentSubLesson.Modifiers(subgroup);
                    var modifierValue = ParseOutModifier(c, it);
                    if (modifierValue.LessonType is { } lessonType)
                    {
                        if (modifiers.LessonType == default)
                        {
                            throw new WrongFormatException();
                        }
                        modifiers.LessonType = lessonType;
                    }
                    else if (modifierValue.Parity is { } parity)
                    {
                        if (modifiers.Parity == default)
                        {
                            throw new WrongFormatException();
                        }
                        modifiers.Parity = parity;
                    }
                    else
                    {
                        throw new WrongFormatException();
                    }

                }

                static SubGroupNumber ParseOutSubGroup(ref ReadOnlySpan<char> it)
                {
                    int sepIndex = it.IndexOf('-');
                    if (sepIndex == -1)
                    {
                        return SubGroupNumber.All;
                    }

                    var subGroupSpan = it[.. sepIndex];
                    it = it[(sepIndex + 1) ..];

                    if (NumberHelper.FromRoman(subGroupSpan) is not { } subGroupNum)
                    {
                        throw new WrongFormatException();
                    }

                    var subGroup = new SubGroupNumber(subGroupNum);
                    return subGroup;
                }

                static (LessonType? LessonType, Parity? Parity) ParseOutModifier(ParsingContext c, ReadOnlySpan<char> it)
                {
                    if (c.Params.LessonTypeParser.Parse(it) is { } lessonType)
                    {
                        return (lessonType, null);
                    }

                    if (c.Params.ParityParser.Parse(it) is { } parity1)
                    {
                        return (null, parity1);
                    }

                    return (null, null);
                }
            }
            case ParsingStep.OptionalSubGroup:
            {
                // These seem to be delimited by a comma.
                if (c.Parser.Current == ',')
                {
                    c.Parser.Move();
                    c.State.Step = ParsingStep.LessonName;
                    break;
                }

                var bparser = c.Parser.BufferedView();
                bparser.Skip(new SkipUntil([':']));
                if (bparser.IsEmpty)
                {
                    c.State.Step = ParsingStep.TeacherName;
                    break;
                }

                var numberSpan = c.Parser.PeekSpanUntilPosition(bparser.Position);
                // TODO: Figure out what to do with named groups? what even are these?
                if (NumberHelper.FromRoman(numberSpan) is { } romanSubGroup)
                {
                    c.State.CurrentSubLesson.SubGroupNumber = new(romanSubGroup);
                }

                c.State.Step = ParsingStep.TeacherName;
                c.Parser.MovePast(bparser.Position);
                break;
            }
            case ParsingStep.TeacherName:
            {
                // TODO: Parse it out as A.Abc?
                var bparser = c.Parser.BufferedView();
                var skipResult = bparser.SkipNotNumbers();
                _ = skipResult;

                var teacherName = SourceUntilExclusive(c.Parser, bparser);
                teacherName = teacherName.TrimEnd();

                c.Parser.MoveTo(bparser.Position);
                c.State.CommonLesson.TeacherName = teacherName;
                c.State.Step = ParsingStep.RoomName;
                break;
            }
            case ParsingStep.RoomName:
            {
                var bparser = c.Parser.BufferedView();
                bparser.SkipNotWhitespace();

                var roomName = SourceUntilExclusive(c.Parser, bparser);
                c.Parser.MoveTo(bparser.Position);
                c.State.CommonLesson.RoomName = roomName;
                c.State.Step = ParsingStep.Output;
                break;
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
        var skipped = p.Skip(new SkipUntil([
            ',',
            ')',
        ]));
        if (!skipped.SkippedAny || skipped.EndOfInput)
        {
            // expected non-empty parens
            throw new WrongFormatException();
        }
    }

    private struct SkipWhitespaceWindow : IShouldSkipSequence
    {
        public bool ShouldSkip(ReadOnlySpan<char> window)
        {
            foreach (var ch in window)
            {
                if (!char.IsWhiteSpace(ch))
                {
                    return false;
                }
            }
            return true;
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
}

public sealed class LessonTypeParser
{
    public static readonly LessonTypeParser Instance = new();
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
    public static readonly ParityParser Instance = new();
    public Parity? Parse(ReadOnlySpan<char> parity)
    {
        static bool Equals1(ReadOnlySpan<char> a, string b)
        {
            return a.Equals(b.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }
        if (Equals1(parity, "par"))
        {
            return Parity.EvenWeek;
        }
        if (Equals1(parity, "imp"))
        {
            return Parity.OddWeek;
        }
        return null;
    }
}

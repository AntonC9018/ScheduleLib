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
    public required List<StringSegment> TeacherNames;
    public required StringSegment RoomName;
    public required StringSegment GroupName;
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
    public ModifiersValue Value = new();
    public SubGroupNumber SubGroupNumber = SubGroupNumber.All;
}

public struct ModifiersValue()
{
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public StringSegment GroupName = default;

    public bool Set(MaybeModifiersValue v)
    {
        if (v.LessonType is { } lessonType)
        {
            LessonType = lessonType;
            return true;
        }
        if (v.Parity is { } parity)
        {
            Parity = parity;
            return true;
        }
        if (v.GroupName != default)
        {
            GroupName = v.GroupName;
            return true;
        }
        return false;
    }
}

public struct MaybeModifiersValue()
{
    public LessonType? LessonType;
    public Parity? Parity;
    public StringSegment GroupName;
}

public struct CommonLessonInParsing()
{
    public TimeOnly? StartTime = null;
    public List<StringSegment> TeacherNames = new();
    public StringSegment RoomName;
}

public enum ParsingStep
{
    Start,
    TimeOverride,
    LessonName,
    OptionalParens,
    OptionalSubGroup,
    TeacherNameList,
    RoomName,
    Output,
}

public readonly record struct StringSegment
{
    private readonly string Source;
    private readonly int Start;
    private readonly int EndExclusive;

    public StringSegment(string source, int start, int endExclusive)
    {
        Source = source;
        Start = start;
        EndExclusive = endExclusive;

        Debug.Assert(Start >= 0);
        Debug.Assert(EndExclusive >= Start);
        Debug.Assert(EndExclusive <= Source.Length);
    }

    public readonly ReadOnlySpan<char> Span => Source.AsSpan(Start, EndExclusive - Start);
    public readonly override string ToString() => Span.ToString();
    public readonly int Length => EndExclusive - Start;

    public readonly StringSegment TrimEnd()
    {
        int end = EndExclusive;
        while (true)
        {
            if (!char.IsWhiteSpace(Source[end - 1]))
            {
                break;
            }
            end--;

            if (end <= Start)
            {
                break;
            }
        }
        return new(Source, Start, end);
    }

    public StringSegment this[Range range]
    {
        get
        {
            var (start, length) = range.GetOffsetAndLength(Length);
            return new(Source, Start + start, Start + start + length);
        }
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

    public bool IsTerminalState
    {
        get
        {
            return Step is ParsingStep.Output
                or ParsingStep.Start
                // In this format, the teacher name and the room are optional
                or ParsingStep.OptionalSubGroup
                or ParsingStep.OptionalParens;
        }
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

                foreach (var x in DoOutput())
                {
                    yield return x;
                }
            }
        }

        if (!state.IsTerminalState)
        {
            throw new WrongFormatException();
        }

        // Special case
        // Only appears to happen in Educatia fizica.
        if (state.IsTerminalState
            && state.Step is not ParsingStep.Output and not ParsingStep.Start)
        {
            foreach (var x in DoOutput())
            {
                yield return x;
            }
        }

        IEnumerable<ParsedLesson> DoOutput()
        {
            foreach (var lesson in state.LessonsInParsing)
            {
                foreach (var modifiers in lesson.AllModifiers)
                {
                    yield return new()
                    {
                        LessonName = lesson.LessonName,
                        TeacherNames = state.CommonLesson.TeacherNames,
                        RoomName = state.CommonLesson.RoomName,
                        Parity = modifiers.Value.Parity,
                        LessonType = modifiers.Value.LessonType,
                        GroupName = modifiers.Value.GroupName,
                        SubGroupNumber = modifiers.SubGroupNumber != SubGroupNumber.All
                            ? modifiers.SubGroupNumber
                            : lesson.SubGroupNumber,
                        StartTime = state.CommonLesson.StartTime,
                    };
                }
            }
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
                    var it = c.Parser.SourceUntilExclusive(bparser);
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

                static void Process(ParsingContext c, StringSegment it)
                {
                    // Check if it's the subgroup form.
                    // ROMAN-modifier
                    var subgroup = ParseOutSubGroup(ref it);
                    ref var modifiers = ref c.State.CurrentSubLesson.Modifiers(subgroup);
                    var modifierValue = ParseOutModifier(c, it);
                    bool somethingSet = modifiers.Value.Set(modifierValue);
                    if (!somethingSet)
                    {
                        throw new WrongFormatException();
                    }
                }

                static SubGroupNumber ParseOutSubGroup(ref StringSegment it)
                {
                    int sepIndex = it.Span.IndexOf('-');
                    if (sepIndex == -1)
                    {
                        return SubGroupNumber.All;
                    }

                    var subGroupSpan = it[.. sepIndex];
                    it = it[(sepIndex + 1) ..];

                    if (NumberHelper.FromRoman(subGroupSpan.Span) is not { } subGroupNum)
                    {
                        throw new WrongFormatException();
                    }

                    var subGroup = new SubGroupNumber(subGroupNum);
                    return subGroup;
                }

                static MaybeModifiersValue ParseOutModifier(ParsingContext c, StringSegment it)
                {
                    if (c.Params.LessonTypeParser.Parse(it.Span) is { } lessonType)
                    {
                        return new()
                        {
                            LessonType = lessonType,
                        };
                    }

                    if (c.Params.ParityParser.Parse(it.Span) is { } parity1)
                    {
                        return new()
                        {
                            Parity = parity1,
                        };
                    }

                    return new()
                    {
                        GroupName = it,
                    };
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
                    c.State.Step = ParsingStep.TeacherNameList;
                    break;
                }

                var numberSpan = c.Parser.PeekSpanUntilPosition(bparser.Position);
                // TODO: Figure out what to do with named groups? what even are these?
                if (NumberHelper.FromRoman(numberSpan) is { } romanSubGroup)
                {
                    c.State.CurrentSubLesson.SubGroupNumber = new(romanSubGroup);
                }

                c.State.Step = ParsingStep.TeacherNameList;
                c.Parser.MovePast(bparser.Position);
                break;
            }
            case ParsingStep.TeacherNameList:
            {
                // TODO: Parse it out as A.Abc?
                var bparser = c.Parser.BufferedView();
                var skipResult = bparser.Skip(new SkipUntilNumberOrComma());
                _ = skipResult;

                var teacherName = SourceUntilExclusive(c.Parser, bparser);
                teacherName = teacherName.TrimEnd();

                c.Parser.MoveTo(bparser.Position);
                c.State.CommonLesson.TeacherNames.Add(teacherName);

                // Keep doing the list if found a comma.
                if (!c.Parser.IsEmpty && c.Parser.Current == ',')
                {
                    c.Parser.Move();
                    break;
                }

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

    public struct SkipUntilNumberOrComma : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (char.IsNumber(ch))
            {
                return false;
            }
            if (ch == ',')
            {
                return false;
            }
            return true;
        }
    }

    public static StringSegment SourceUntilExclusive(this Parser a, Parser b)
    {
        Debug.Assert(ReferenceEquals(a.Source, b.Source));

        int start = a.Position;
        int end = b.Position;
        return new(a.Source, start, end);
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

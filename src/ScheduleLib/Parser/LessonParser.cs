using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Generation;

namespace ScheduleLib.Parsing.Lesson;

public struct ParseLessonsParams
{
    public required LessonTypeParser LessonTypeParser;
    public required ParityParser ParityParser;
    public required IEnumerable<string> Lines;
}

public struct ParsedLesson()
{
    public required ReadOnlyMemory<char> LessonName;
    public required List<ReadOnlyMemory<char>> TeacherNames;
    public required ReadOnlyMemory<char> RoomName;
    public required ReadOnlyMemory<char> GroupName;
    public TimeOnly? StartTime = null;
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public SubGroup SubGroup = SubGroup.All;
}

public struct SubLessonInParsing()
{
    public ReadOnlyMemory<char> LessonName = default;
    public SubGroup SubGroup = SubGroup.All;
    public List<Modifiers> AllModifiers = new();

    public ref Modifiers Modifiers(SubGroup subGroup = default)
    {
        if (subGroup == default)
        {
            subGroup = SubGroup.All;
        }

        {
            var mods = CollectionsMarshal.AsSpan(AllModifiers);
            for (int i = 0; i < mods.Length; i++)
            {
                ref var it = ref mods[i];
                if (it.SubGroup == subGroup)
                {
                    return ref it;
                }
            }
        }

        {
            AllModifiers.Add(new()
            {
                SubGroup = subGroup,
            });
            return ref CollectionsMarshal.AsSpan(AllModifiers)[^1];
        }
    }
}

public struct Modifiers()
{
    public ModifiersValue Value = new();
    public SubGroup SubGroup = SubGroup.All;
}

public struct ModifiersValue()
{
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public ReadOnlyMemory<char> GroupName = default;

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
        if (!v.GroupName.IsEmpty)
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
    public ReadOnlyMemory<char> GroupName;
}

public struct CommonLessonInParsing()
{
    public TimeOnly? StartTime = null;
    public List<ReadOnlyMemory<char>> TeacherNames = new();
    public ReadOnlyMemory<char> RoomName;
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
                        SubGroup = modifiers.SubGroup != SubGroup.All
                            ? modifiers.SubGroup
                            : lesson.SubGroup,
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
                name = CleanCourseName(name);

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
                    it = it.Trim();
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

                static void Process(ParsingContext c, ReadOnlyMemory<char> it)
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

                static SubGroup ParseOutSubGroup(ref ReadOnlyMemory<char> it)
                {
                    int sepIndex = it.Span.IndexOf('-');
                    if (sepIndex == -1)
                    {
                        return SubGroup.All;
                    }

                    var subGroupSpan = it[.. sepIndex];
                    it = it[(sepIndex + 1) ..];

                    var subGroup = new SubGroup(subGroupSpan.Span.ToString());
                    return subGroup;
                }

                static MaybeModifiersValue ParseOutModifier(ParsingContext c, ReadOnlyMemory<char> it)
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
                c.State.CurrentSubLesson.SubGroup = new(numberSpan.ToString());

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
                teacherName = CleanTeacherName(teacherName);

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
                if (!char.IsNumber(bparser.Current))
                {
                    c.State.Step = ParsingStep.Output;
                    break;
                }

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

    public static ReadOnlyMemory<char> SourceUntilExclusive(this Parser a, Parser b)
    {
        Debug.Assert(ReferenceEquals(a.Source, b.Source));

        int start = a.Position;
        int end = b.Position;
        return a.Source.AsMemory(start .. end);
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

    private ref struct SkipUntil : IShouldSkip
    {
        private readonly ReadOnlySpan<char> _chars;
        public SkipUntil(ReadOnlySpan<char> chars) => _chars = chars;
        public bool ShouldSkip(char ch) => !_chars.Contains(ch);
    }

    private static ReadOnlyMemory<char> CleanCourseName(ReadOnlyMemory<char> input)
    {
        input = input.TrimEnd();

        int newLength = NewLength();
        var ret = string.Create(newLength, input, static (output, input) =>
        {
            int writePos = 0;
            foreach (var ch in input.Span.WordsSeparatedWithSpaces())
            {
                output[writePos] = ch;
                writePos++;
            }

            Debug.Assert(writePos == output.Length);
        });
        return ret.AsMemory();

        int NewLength()
        {
            int count = 0;
            foreach (var ch in input.Span.WordsSeparatedWithSpaces())
            {
                // For each dot in the middle, we need to add a space
                _ = ch;
                count++;
            }
            return count;
        }
    }


    private static ReadOnlyMemory<char> CleanTeacherName(ReadOnlyMemory<char> input)
    {
        input = input.TrimEnd();

        var spaceCount = input.Span.Count(' ');
        if (spaceCount == 0)
        {
            return input;
        }
        var ret = string.Create(input.Length - spaceCount, input, static (output, input) =>
        {
            var inputSpan = input.Span;

            int writePos = 0;
            for (int readPos = 0; readPos < input.Length; readPos++)
            {
                if (inputSpan[readPos] != ' ')
                {
                    output[writePos] = inputSpan[readPos];
                    writePos++;
                }
            }

            Debug.Assert(writePos == output.Length);
        });
        return ret.AsMemory();
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

public static class StringCleanHelper
{
    public static WithoutConsecutiveSpacesEnumerable WithoutConsecutiveSpaces(this ReadOnlySpan<char> s)
    {
        return new(s);
    }
    public static WordsSeparatedWithSpacesEnumerable WordsSeparatedWithSpaces(this ReadOnlySpan<char> s)
    {
        return new(s);
    }
}

public readonly ref struct WordsSeparatedWithSpacesEnumerable
{
    private readonly ReadOnlySpan<char> _str;
    public WordsSeparatedWithSpacesEnumerable(ReadOnlySpan<char> str) => _str = str;
    public Enumerator GetEnumerator() => new(_str);

    public ref struct Enumerator
    {
        private WithoutConsecutiveSpacesEnumerable.Enumerator _withoutSpaces;
        private bool _shouldOutputSpace;

        public Enumerator(ReadOnlySpan<char> str)
        {
            _withoutSpaces = new(str);
            _shouldOutputSpace = false;
        }

        public readonly char Current
        {
            get
            {
                if (_shouldOutputSpace)
                {
                    return ' ';
                }
                return _withoutSpaces.Current;
            }
        }

        public bool MoveNext()
        {
            if (_shouldOutputSpace)
            {
                _shouldOutputSpace = false;
                return true;
            }

            bool isPrevPunctuation = _withoutSpaces.IsInitialized
                && char.IsPunctuation(_withoutSpaces.Current);
            if (!_withoutSpaces.MoveNext())
            {
                return false;
            }

            bool isCurrentSpace = _withoutSpaces.Current == ' ';
            if (!isCurrentSpace && isPrevPunctuation)
            {
                _shouldOutputSpace = true;
            }

            return true;
        }
    }
}

public readonly ref struct WithoutConsecutiveSpacesEnumerable
{
    private readonly ReadOnlySpan<char> _str;
    public WithoutConsecutiveSpacesEnumerable(ReadOnlySpan<char> str) => _str = str;
    public Enumerator GetEnumerator() => new(_str);

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<char> _str;
        private bool _isSpace;
        private int _index;

        public Enumerator(ReadOnlySpan<char> str)
        {
            _str = str;
            _isSpace = false;
            _index = -1;
        }

        public readonly bool IsInitialized => _index != -1;

        public readonly bool IsDone
        {
            get
            {
                return _index >= _str.Length;
            }
        }

        public readonly char Current => _str[_index];

        public bool MoveNext()
        {
            while (true)
            {
                _index++;
                if (IsDone)
                {
                    return false;
                }

                bool isCurrentSpace = Current is ' ' or '\t' or '\r' or '\n';
                if (_isSpace && isCurrentSpace)
                {
                    continue;
                }

                _isSpace = isCurrentSpace;
                return true;
            }
        }
    }
}

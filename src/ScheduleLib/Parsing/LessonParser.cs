// TODO: Remove the use of lists.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using ScheduleLib.Generation;

namespace ScheduleLib.Parsing.Lesson;

public struct ParseLessonsParams()
{
    public LessonTypeParser LessonTypeParser = LessonTypeParser.Instance;
    public ParityParser ParityParser = ParityParser.Instance;
    public required IEnumerable<string> Lines;
}

public readonly struct ModifiersList<T, TImpl>()
    where T : struct
    where TImpl : struct, IModifiersImpl<T>
{
    private readonly List<T> _list = new();

    public List<T>.Enumerator GetEnumerator() => _list.GetEnumerator();
    public bool IsEmpty => _list.Count == 0;

    private TImpl Impl => new();

    public void Clear()
    {
        _list.Clear();
    }

    public bool OnlyHasAllSubGroup()
    {
        if (_list.Count > 1)
        {
            return false;
        }
        return Impl.SubGroup(Ref(0)) == SubGroup.All;
    }

    public ref T Ref(int index)
    {
        return ref CollectionsMarshal.AsSpan(_list)[index];
    }

    public int FindIndex(SubGroup subGroup)
    {
        var mods = CollectionsMarshal.AsSpan(_list);
        for (int i = 0; i < mods.Length; i++)
        {
            ref var it = ref mods[i];
            if (Impl.SubGroup(it) == subGroup)
            {
                return i;
            }
        }
        return -1;
    }

    public int FindOrAdd(SubGroup subGroup)
    {
        int index = FindIndex(subGroup);
        if (index != -1)
        {
            return index;
        }

        var it = Impl.Create(subGroup);
        _list.Add(it);
        return _list.Count - 1;
    }

    public ref T Ref(SubGroup subGroup)
    {
        if (subGroup == default)
        {
            subGroup = SubGroup.All;
        }

        int index = FindOrAdd(subGroup);
        return ref Ref(index);
    }
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
    public ModifiersList<SubLessonModifiers, SubLessonModifiers.Impl> Modifiers = new();
}

public struct GeneralModifiersValue()
{
    public LessonType LessonType = LessonType.Unspecified;
    public Parity Parity = Parity.EveryWeek;
    public ReadOnlyMemory<char> GroupName = default;

    internal bool Set(MaybeGeneralModifiersValue v)
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

    internal void UpdateIfNotDefault(in GeneralModifiersValue v)
    {
        if (v.LessonType != LessonType.Unspecified)
        {
            LessonType = v.LessonType;
        }
        if (v.Parity != Parity.EveryWeek)
        {
            Parity = v.Parity;
        }
        if (!v.GroupName.IsEmpty)
        {
            GroupName = v.GroupName;
        }
    }
}

public struct SpecificModifiersValue()
{
    public List<ReadOnlyMemory<char>> TeacherNames = new();
    public ReadOnlyMemory<char> RoomName = default;

    public void UpdateIfNotDefault(in SpecificModifiersValue v)
    {
        if (v.TeacherNames.Count != 0)
        {
            TeacherNames = v.TeacherNames;
        }
        if (!v.RoomName.IsEmpty)
        {
            RoomName = v.RoomName;
        }
    }
}

public interface IModifiersImpl<T>
{
    SubGroup SubGroup(in T v);
    T Create(SubGroup subGroup);
}

public struct DefaultModifiersValue()
{
    public GeneralModifiersValue General = new();
    public SpecificModifiersValue Specific = new();
}

public struct DefaultModifiers()
{
    public DefaultModifiersValue Value = new();
    public required SubGroup SubGroup { get; init; }

    [UnscopedRef] public ref GeneralModifiersValue General => ref Value.General;
    [UnscopedRef] public ref SpecificModifiersValue Specific => ref Value.Specific;

    public struct Impl : IModifiersImpl<DefaultModifiers>
    {
        public SubGroup SubGroup(in DefaultModifiers v) => v.SubGroup;
        public DefaultModifiers Create(SubGroup subGroup)
        {
            return new DefaultModifiers { SubGroup = subGroup };
        }
    }
}

public struct SubLessonModifiers()
{
    public GeneralModifiersValue General = new();
    public required SubGroup SubGroup { get; init; }

    public struct Impl : IModifiersImpl<SubLessonModifiers>
    {
        public SubGroup SubGroup(in SubLessonModifiers v) => v.SubGroup;
        public SubLessonModifiers Create(SubGroup subGroup)
        {
            return new SubLessonModifiers { SubGroup = subGroup };
        }
    }
}

internal struct MaybeGeneralModifiersValue()
{
    public LessonType? LessonType;
    public Parity? Parity;
    public ReadOnlyMemory<char> GroupName;
}

internal struct CommonLessonInParsing()
{
    public TimeOnly? StartTime = null;
}

public enum ParsingStep
{
    Start,
    TimeOverride,
    LessonName,
    OptionalParens,
    OptionalSubGroup,
    TeacherNameList,
    OptionalParensBeforeRoom,
    RoomName,
    MaybeSubGroupAgain,
    Output,
}

internal struct ParsingState()
{
    public ParsingStep Step = ParsingStep.Start;
    public CommonLessonInParsing CommonLesson = new();
    public ModifiersList<DefaultModifiers, DefaultModifiers.Impl> DefaultModifiers = new();
    public List<SubLessonInParsing> LessonsInParsing = new();
    public int LastModiferIndex = -1;

    public ref SubLessonInParsing CurrentSubLesson => ref CollectionsMarshal.AsSpan(LessonsInParsing)[^1];
    public ref DefaultModifiers LastModifiers => ref DefaultModifiers.Ref(LastModiferIndex);

    public void Reset()
    {
        Step = ParsingStep.TimeOverride;
        LessonsInParsing.Clear();
        DefaultModifiers.Clear();
        CommonLesson = new();
        LastModiferIndex = 0;
    }

    public bool IsTerminalState
    {
        get
        {
            return Step is ParsingStep.Output
                or ParsingStep.Start
                // In this format, the teacher name and the room are optional
                or ParsingStep.OptionalSubGroup
                or ParsingStep.OptionalParens
                or ParsingStep.OptionalParensBeforeRoom
                or ParsingStep.RoomName
                or ParsingStep.MaybeSubGroupAgain;
        }
    }
}

public sealed class WrongFormatException : Exception
{
    internal WrongFormatException(string? s = null) : base(s)
    {
    }


    [DoesNotReturn]
    public static void ThrowEmptyCourseName() => throw new WrongFormatException("Empty course name");
}

internal ref struct ParsingContext
{
    public required ref ParseLessonsParams Params;
    public required ref ParsingState State;
    public required ref Parser Parser;
}
public static class ParsingHelper1
{
    public static ReadOnlyMemory<char> SourceUntilExclusive(this Parser a, Parser b)
    {
        Debug.Assert(ReferenceEquals(a.Source, b.Source));

        int start = a.Position;
        int end = b.Position;
        return a.Source.AsMemory(start .. end);
    }
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
                parser.Skip(new SkipWhitespaceButNotUnderscore());
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
            var allDefaultIndex = state.DefaultModifiers.FindIndex(SubGroup.All);
            DefaultModifiersValue allDefaults;
            if (allDefaultIndex != -1)
            {
                allDefaults = state.DefaultModifiers.Ref(allDefaultIndex).Value;
            }
            else
            {
                allDefaults = new();
            }

            foreach (var lesson in state.LessonsInParsing)
            {
                var allFallback = allDefaults;

                var allIndex = lesson.Modifiers.FindIndex(SubGroup.All);
                if (allIndex != -1)
                {
                    ref var all = ref lesson.Modifiers.Ref(allIndex);
                    allFallback.General.UpdateIfNotDefault(all.General);
                }

                {
                    bool shouldDoAllSubGroup = allDefaultIndex != -1;

                    foreach (var mod in lesson.Modifiers)
                    {
                        var v = allFallback;

                        if (mod.SubGroup == SubGroup.All)
                        {
                            if (!shouldDoAllSubGroup)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            var defaultIndex = state.DefaultModifiers.FindIndex(mod.SubGroup);
                            if (defaultIndex != -1)
                            {
                                ref var def = ref state.DefaultModifiers.Ref(defaultIndex);
                                v.General.UpdateIfNotDefault(def.General);
                                v.Specific.UpdateIfNotDefault(def.Specific);
                            }
                        }

                        v.General.UpdateIfNotDefault(mod.General);
                        yield return Output(mod.SubGroup, v, lesson.LessonName);
                    }
                }

                foreach (var defaultMod in state.DefaultModifiers)
                {
                    if (defaultMod.SubGroup == SubGroup.All)
                    {
                        continue;
                    }

                    var lessonModIndex = lesson.Modifiers.FindIndex(defaultMod.SubGroup);
                    if (lessonModIndex != -1)
                    {
                        continue;
                    }

                    var v = allFallback;
                    v.General.UpdateIfNotDefault(defaultMod.General);
                    v.Specific.UpdateIfNotDefault(defaultMod.Specific);

                    yield return Output(defaultMod.SubGroup, v, lesson.LessonName);
                }
            }


            ParsedLesson Output(
                SubGroup subGroup,
                in DefaultModifiersValue v,
                ReadOnlyMemory<char> lessonName)
            {
                return new()
                {
                    LessonName = lessonName,
                    StartTime = state.CommonLesson.StartTime,
                    LessonType = v.General.LessonType,
                    Parity = v.General.Parity,
                    SubGroup = subGroup,
                    GroupName = v.General.GroupName,
                    TeacherNames = v.Specific.TeacherNames,
                    RoomName = v.Specific.RoomName,
                };
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
                bparser.SkipUntil(['(', ',']);

                if (!bparser.IsEmpty && bparser.Current == '(')
                {
                    var bparserPastParen = bparser.BufferedView();
                    while (true)
                    {
                        // Skip until the last parenthesized group
                        bparserPastParen.Move();
                        // The comma is needed here because lessons might be separated by commas.
                        // Because the parser cannot backtrack, commas can't appear in lesson names after parens.
                        var result = bparserPastParen.SkipUntil(['(', ',']);
                        if (result.EndOfInput || bparserPastParen.Current == ',')
                        {
                            break;
                        }

                        bparser = bparserPastParen;
                    }
                }

                var name = c.Parser.SourceUntilExclusive(bparser);
                if (name.IsEmpty)
                {
                    WrongFormatException.ThrowEmptyCourseName();
                }
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
            case ParsingStep.OptionalParensBeforeRoom:
            {
                if (EarlyExitNextStep(c))
                {
                    break;
                }

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
                break;

                static bool EarlyExitNextStep(ParsingContext c)
                {
                    if (c.Parser.Current == '(')
                    {
                        c.Parser.Move();
                        return false;
                    }
                    if (c.State.Step == ParsingStep.OptionalParensBeforeRoom)
                    {
                        // I don't even know how to get here.
                        Debug.Assert(c.Parser.Current != ',');

                        c.State.Step = ParsingStep.RoomName;
                        return true;
                    }
                    if (c.Parser.Current == ',')
                    {
                        c.State.Step = ParsingStep.LessonName;
                        c.Parser.Move();
                        return true;
                    }
                    c.State.Step = ParsingStep.OptionalSubGroup;
                    return true;
                }

                static void Process(ParsingContext c, ReadOnlyMemory<char> it)
                {
                    // Check if it's the subgroup form.
                    // ROMAN-modifier
                    var subgroup = ParseOutSubGroup(ref it);
                    ref var modifiers = ref GetCurrentModifiers(c, subgroup);
                    var modifierValue = ParseOutModifier(c, it);
                    bool somethingSet = modifiers.Set(modifierValue);
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

                static ref GeneralModifiersValue GetCurrentModifiers(ParsingContext c, SubGroup parsedSubGroup)
                {
                    bool isParsingInsideSubgroupAlready = c.State.Step == ParsingStep.OptionalParensBeforeRoom;
                    if (!isParsingInsideSubgroupAlready)
                    {
                        ref var modifiers = ref c.State.CurrentSubLesson.Modifiers.Ref(parsedSubGroup).General;
                        return ref modifiers;
                    }

                    if (parsedSubGroup != SubGroup.All)
                    {
                        throw new WrongFormatException();
                    }

                    return ref c.State.LastModifiers.General;
                }

                static MaybeGeneralModifiersValue ParseOutModifier(ParsingContext c, ReadOnlyMemory<char> it)
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
            case ParsingStep.MaybeSubGroupAgain:
            {
                if (c.State.Step == ParsingStep.OptionalSubGroup)
                {
                    // Lesson names might be delimited by a comma.
                    if (c.Parser.Current == ',')
                    {
                        c.Parser.Move();
                        c.State.Step = ParsingStep.LessonName;
                        break;
                    }
                }
                else
                {
                    // Just ignore the comma if we're here again.
                    // This handles the case where there are multiple subgroups.
                    if (c.Parser.Current == ',')
                    {
                        c.Parser.Move();
                        // Skip whitespace.
                        break;
                    }
                }

                var bparser = c.Parser.BufferedView();
                bparser.SkipUntil([':']);
                if (!IsSubGroup(ref bparser))
                {
                    if (c.State.Step == ParsingStep.MaybeSubGroupAgain)
                    {
                        c.State.Step = ParsingStep.Output;
                        break;
                    }
                    // Maybe should check how it was added and give an error if it was
                    // added through "subgroup:" rather than "subgroup-modifier" syntax.
                    c.State.LastModiferIndex = c.State.DefaultModifiers.FindOrAdd(SubGroup.All);
                    c.State.Step = ParsingStep.TeacherNameList;
                    break;
                }

                var numberSpan = c.Parser.PeekSpanUntilPosition(bparser.Position);
                var subgroup = new SubGroup(numberSpan.ToString());
                c.State.LastModiferIndex = c.State.DefaultModifiers.FindOrAdd(subgroup);

                c.State.Step = ParsingStep.TeacherNameList;
                c.Parser.MovePast(bparser.Position);
                break;

                static bool IsSubGroup(ref Parser parser)
                {
                    if (parser.IsEmpty)
                    {
                        return false;
                    }
                    if (!parser.CanPeekAt(1))
                    {
                        return false;
                    }
                    if (parser.PeekAt(1) == ' ')
                    {
                        return true;
                    }
                    return false;
                }
            }
            case ParsingStep.TeacherNameList:
            {
                // Maybe parse it out as A.Abc?
                var bparser = c.Parser.BufferedView();
                var skipResult = bparser.Skip(new SkipUntilNumberOrCommaOrUnderscoreOrParen());
                _ = skipResult;

                var teacherName = c.Parser.SourceUntilExclusive(bparser);
                teacherName = CleanTeacherName(teacherName);

                c.Parser.MoveTo(bparser.Position);
                c.State.LastModifiers.Specific.TeacherNames.Add(teacherName);

                // Keep doing the list if found a comma.
                if (!c.Parser.IsEmpty && c.Parser.Current == ',')
                {
                    c.Parser.Move();
                    break;
                }

                c.State.Step = ParsingStep.OptionalParensBeforeRoom;
                break;
            }
            case ParsingStep.RoomName:
            {
                var bparser = c.Parser.BufferedView();
                if (!IsRoomStart(bparser.Current))
                {
                    c.State.Step = ParsingStep.MaybeSubGroupAgain;
                    break;
                }

                bparser.Skip(new NotWhitespaceOrCommaSkip());

                var roomName = c.Parser.SourceUntilExclusive(bparser);
                c.Parser.MoveTo(bparser.Position);
                c.State.LastModifiers.Specific.RoomName = roomName;
                c.State.Step = ParsingStep.MaybeSubGroupAgain;
                break;
            }
        }
    }

    private static bool IsRoomStart(char ch)
    {
        if (char.IsNumber(ch))
        {
            return true;
        }
        if (ch == '_')
        {
            return true;
        }
        return false;
    }

    private struct SkipWhitespaceButNotUnderscore : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (ch == '_')
            {
                return false;
            }
            if (char.IsWhiteSpace(ch))
            {
                return true;
            }
            return false;
        }
    }

    private struct SkipUntilNumberOrCommaOrUnderscoreOrParen : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (char.IsNumber(ch))
            {
                return false;
            }
            if (ch == '_')
            {
                return false;
            }
            if (ch == ',')
            {
                return false;
            }
            if (ch == '(')
            {
                return false;
            }
            return true;
        }
    }

    private struct NotWhitespaceOrCommaSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => !char.IsWhiteSpace(ch) && ch != ',';
    }

    private static void SkipParenListItem(ref Parser p)
    {
        var skipped = p.SkipUntil([',', ')']);
        if (!skipped.SkippedAny || skipped.EndOfInput)
        {
            // expected non-empty parens
            throw new WrongFormatException();
        }
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
        if (Equals1(parity, "impar"))
        {
            return Parity.OddWeek;
        }
        return null;
    }
}

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
    public RoomParser RoomParser = RoomParser.Instance;
    public required IEnumerable<string> Lines;
}

public readonly struct DefaultModifiersList()
{
    private readonly List<DefaultModifiers> _list = new();

    public List<DefaultModifiers>.Enumerator GetEnumerator() => _list.GetEnumerator();
    public bool IsEmpty => _list.Count == 0;

    public void Clear()
    {
        _list.Clear();
    }

    public ref DefaultModifiers Ref(int index)
    {
        return ref CollectionsMarshal.AsSpan(_list)[index];
    }

    public bool HasOtherThanAllSubGroup()
    {
        if (_list.Count != 1)
        {
            return true;
        }
        return Ref(0).SubGroup != SubGroup.All;
    }

    public int FindIndex(SubGroup subGroup)
    {
        var mods = CollectionsMarshal.AsSpan(_list);
        for (int i = 0; i < mods.Length; i++)
        {
            ref var it = ref mods[i];
            if (it.SubGroup == subGroup)
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

        var it = new DefaultModifiers
        {
            SubGroup = subGroup,
        };
        _list.Add(it);
        return _list.Count - 1;
    }
}

public readonly struct SubLessonModifiersList()
{
    private readonly List<SubLessonModifiers> _list = new();

    public List<SubLessonModifiers>.Enumerator GetEnumerator() => _list.GetEnumerator();

    public bool IsEmpty => _list.Count == 0;

    public ref SubLessonModifiers Ref(int index)
    {
        return ref CollectionsMarshal.AsSpan(_list)[index];
    }

    public bool HasOtherThanDefaultKey()
    {
        if (_list.Count != 1)
        {
            return true;
        }
        return Ref(0).Key != SubLessonModifiersKey.Default;
    }

    public int FindIndex(SubLessonModifiersKey key)
    {
        var mods = CollectionsMarshal.AsSpan(_list);
        for (int i = 0; i < mods.Length; i++)
        {
            ref var it = ref mods[i];
            if (it.Key == key)
            {
                return i;
            }
        }
        return -1;
    }

    public int FindOrAdd(SubLessonModifiersKey key)
    {
        int index = FindIndex(key);
        if (index != -1)
        {
            return index;
        }

        var it = new SubLessonModifiers
        {
            Key = key,
        };
        _list.Add(it);
        return _list.Count - 1;
    }

    public ref SubLessonModifiers Ref(SubLessonModifiersKey key)
    {
        if (key == default)
        {
            key = new();
        }

        int index = FindOrAdd(key);
        return ref Ref(index);
    }
}

public struct ParsedLesson()
{
    public required ReadOnlyMemory<char> LessonName;
    public required List<TeacherName> TeacherNames;
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
    public SubLessonModifiersList Modifiers = new();
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

public struct TeacherName
{
    public ReadOnlyMemory<char> ShortFirstName;
    public ReadOnlyMemory<char> LastName;
}

public struct SpecificModifiersValue()
{
    public List<TeacherName> TeacherNames = new();
    public ReadOnlyMemory<char> RoomName = default;

    public readonly ref TeacherName LastTeacher
    {
        get
        {
            return ref CollectionsMarshal.AsSpan(TeacherNames)[^1];
        }
    }

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

    public readonly ref TeacherName NewTeacher()
    {
        CollectionsMarshal.SetCount(TeacherNames, TeacherNames.Count + 1);
        ref var ret = ref CollectionsMarshal.AsSpan(TeacherNames)[^1];
        ret = default;
        return ref ret;
    }
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
}

public readonly record struct SubLessonModifiersKey()
{
    public static SubLessonModifiersKey Default => new();
    public SubGroup SubGroup { get; init; } = SubGroup.All;
    public LessonType LessonType { get; init; } = LessonType.Unspecified;
}

public struct SubLessonModifiers()
{
    public GeneralModifiersValue General = new();
    public required SubLessonModifiersKey Key { get; init; }
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
    public bool HasStar = false;
}

public enum ParsingStep
{
    Start,
    TimeOverride,
    OptionalStarBeforeLessonName,
    LessonName,
    OptionalParens,
    OptionalSubGroup,
    RequiredTeacherNameOrRoomName,
    OptionalTeacherNameOrRoomName,
    TeacherLastName,
    OptionalParensBeforeRoom,
    OptionalRoomName,
    MaybeSubGroupAgain,
    Output,
}

internal struct ParsingState()
{
    public ParsingStep Step = ParsingStep.Start;
    public CommonLessonInParsing CommonLesson = new();
    public DefaultModifiersList DefaultModifiers = new();
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
                or ParsingStep.OptionalTeacherNameOrRoomName
                or ParsingStep.OptionalRoomName
                or ParsingStep.MaybeSubGroupAgain;
        }
    }
}

public sealed class RoomAlreadySpecifiedException : WrongFormatException
{
    internal RoomAlreadySpecifiedException() : base("Room already specified")
    {
    }
}

// TODO: Should be abstract
public class WrongFormatException : Exception
{
    internal WrongFormatException(string? s = null) : base(s)
    {
    }


    [DoesNotReturn]
    internal static void ThrowEmptyCourseName() => throw new WrongFormatException("Empty course name");

    [DoesNotReturn]
    internal static void ThrowRoomAlreadySpecified() => throw new RoomAlreadySpecifiedException();

    [DoesNotReturn]
    internal static void ThrowUnclosedParenInLessonName() => throw new WrongFormatException("Unclosed paren in lesson name");
}

internal ref struct ParsingContext
{
    public required ref readonly ParseLessonsParams Params;
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

            bool defaultHasOtherThanAllSubGroup = state.DefaultModifiers.HasOtherThanAllSubGroup();

            foreach (var lesson in state.LessonsInParsing)
            {
                var allFallback = allDefaults;

                var allIndex = lesson.Modifiers.FindIndex(SubLessonModifiersKey.Default);
                if (allIndex != -1)
                {
                    ref var all = ref lesson.Modifiers.Ref(allIndex);
                    allFallback.General.UpdateIfNotDefault(all.General);
                }

                {
                    bool shouldNotOutputDefaultKey = defaultHasOtherThanAllSubGroup
                        || lesson.Modifiers.HasOtherThanDefaultKey();

                    foreach (var mod in lesson.Modifiers)
                    {
                        bool isDefaultKey = mod.Key == SubLessonModifiersKey.Default;
                        if (isDefaultKey && shouldNotOutputDefaultKey)
                        {
                            continue;
                        }

                        var v = allFallback;

                        // These are already in the fallback if it's the default.
                        if (!isDefaultKey)
                        {
                            var defaultIndex = state.DefaultModifiers.FindIndex(mod.Key.SubGroup);
                            if (defaultIndex != -1)
                            {
                                ref var def = ref state.DefaultModifiers.Ref(defaultIndex);
                                v.General.UpdateIfNotDefault(def.General);
                                v.Specific.UpdateIfNotDefault(def.Specific);
                            }
                            v.General.UpdateIfNotDefault(mod.General);

                            if (mod.Key.LessonType != LessonType.Unspecified)
                            {
                                if (mod.General.LessonType != LessonType.Unspecified)
                                {
                                    throw new WrongFormatException();
                                }

                                v.General.LessonType = mod.Key.LessonType;
                            }
                        }

                        yield return Output(mod.Key.SubGroup, v, lesson.LessonName);
                    }
                }

                foreach (var defaultMod in state.DefaultModifiers)
                {
                    if (defaultMod.SubGroup == SubGroup.All
                        && !lesson.Modifiers.IsEmpty)
                    {
                        continue;
                    }

                    var key = new SubLessonModifiersKey
                    {
                        SubGroup = defaultMod.SubGroup,
                        LessonType = LessonType.Unspecified,
                    };
                    var lessonModIndex = lesson.Modifiers.FindIndex(key);
                    if (lessonModIndex != -1)
                    {
                        continue;
                    }

                    var v = allFallback;
                    v.General.UpdateIfNotDefault(defaultMod.General);
                    v.Specific.UpdateIfNotDefault(defaultMod.Specific);

                    yield return Output(defaultMod.SubGroup, v, lesson.LessonName);
                }

                if (lesson.Modifiers.IsEmpty
                    && state.DefaultModifiers.IsEmpty)
                {
                    yield return Output(
                        SubGroup.All,
                        allFallback,
                        lesson.LessonName);
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

                c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                break;
            }
            case ParsingStep.OptionalStarBeforeLessonName:
            {
                if (c.Parser.Current == '*')
                {
                    c.State.CommonLesson.HasStar = true;
                    c.Parser.Move();
                }
                c.State.Step = ParsingStep.LessonName;
                break;
            }
            case ParsingStep.LessonName:
            {
                // Until end of line or paren
                var bparser = c.Parser.BufferedView();
                var result = bparser.SkipUntilLessonEnd();

                // Skip until the last parenthesized group
                if (result.IsOpeningParen)
                {
                    var bparserTemp = bparser.BufferedView();

                    while (true)
                    {
                        var resultClosing = bparserTemp.SkipUntil([')']);
                        if (resultClosing.EndOfInput)
                        {
                            WrongFormatException.ThrowUnclosedParenInLessonName();
                            break;
                        }

                        bparserTemp.Move();

                        var result1 = bparserTemp.SkipUntilLessonEnd();

                        // This is not the last parenthesized group
                        if (result1.IsOpeningParen)
                        {
                            bparser.MoveTo(bparserTemp.Position);
                            bparserTemp.Move();
                            continue;
                        }

                        // This is the last parenthesized group
                        // Do not include it in bparser
                        break;
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

                        c.State.Step = ParsingStep.OptionalRoomName;
                        return true;
                    }
                    if (c.Parser.Current == ',')
                    {
                        c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
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
                    var key = ParseOutKey(ref it, c.Params.LessonTypeParser);
                    ref var modifiers = ref GetCurrentModifiers(c, key);
                    var modifierValue = ParseOutModifier(c, it);
                    bool somethingSet = modifiers.Set(modifierValue);
                    if (!somethingSet)
                    {
                        throw new WrongFormatException();
                    }
                }

                static SubLessonModifiersKey ParseOutKey(
                    ref ReadOnlyMemory<char> it,
                    LessonTypeParser lessonTypeParser)
                {
                    int sepIndex = it.Span.IndexOf('-');
                    if (sepIndex == -1)
                    {
                        return new();
                    }

                    var s = it.Span[.. sepIndex];
                    it = it[(sepIndex + 1) ..];

                    if (lessonTypeParser.Parse(s) is { } lessonType)
                    {
                        return new()
                        {
                            LessonType = lessonType,
                        };
                    }

                    var subGroup = new SubGroup(s.ToString());
                    return new()
                    {
                        SubGroup = subGroup,
                    };
                }

                static ref GeneralModifiersValue GetCurrentModifiers(ParsingContext c, SubLessonModifiersKey key)
                {
                    bool isParsingInsideSubgroupAlready = c.State.Step == ParsingStep.OptionalParensBeforeRoom;
                    if (!isParsingInsideSubgroupAlready)
                    {
                        ref var modifiers = ref c.State.CurrentSubLesson.Modifiers.Ref(key).General;
                        return ref modifiers;
                    }

                    if (key != SubLessonModifiersKey.Default)
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
                        c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
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
                    c.State.Step = ParsingStep.OptionalTeacherNameOrRoomName;
                    break;
                }

                var numberSpan = c.Parser.PeekSpanUntilPosition(bparser.Position);
                var subgroup = new SubGroup(numberSpan.ToString());
                c.State.LastModiferIndex = c.State.DefaultModifiers.FindOrAdd(subgroup);

                c.State.Step = ParsingStep.OptionalTeacherNameOrRoomName;
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
            case ParsingStep.RequiredTeacherNameOrRoomName:
            case ParsingStep.OptionalTeacherNameOrRoomName:
            {
                if (TryParseAndSetRoomName(c))
                {
                    c.Parser.SkipWhitespace();
                    if (c.Parser.IsEmpty)
                    {
                        AdvanceStepAfterRoom(c);
                        break;
                    }
                    if (c.Parser.Current == ',')
                    {
                        // Continue the list.
                        c.State.Step = ParsingStep.RequiredTeacherNameOrRoomName;
                        c.Parser.Move();
                        break;
                    }

                    AdvanceStepAfterRoom(c);
                    break;
                }

                var bparser = c.Parser.BufferedView();
                var skipResult = bparser.Skip(new SkipUntilDotOrSpaceOrComma());
                if (!skipResult.SkippedAny)
                {
                    if (c.State.Step == ParsingStep.RequiredTeacherNameOrRoomName)
                    {
                        // Required teacher name after comma
                        throw new WrongFormatException();
                    }

                    // We've already tried for room name.
                    AdvanceStepAfterRoom(c);
                    break;
                }

                ref var teacher = ref c.State.LastModifiers.Specific.NewTeacher();

                // Last name
                if (skipResult.EndOfInput
                    || bparser.Current is ' ' or ',')
                {
                    var lastName = c.Parser.SourceUntilExclusive(bparser);
                    teacher.LastName = lastName;
                    NextStep(c);

                    void NextStep(in ParsingContext c1)
                    {
                        if (skipResult.EndOfInput)
                        {
                            c1.State.Step = ParsingStep.OptionalRoomName;
                            return;
                        }
                        if (bparser.Current == ' ')
                        {
                            c1.State.Step = ParsingStep.OptionalParensBeforeRoom;
                            return;
                        }

                        Debug.Assert(bparser.Current == ',');
                        c1.State.Step = ParsingStep.RequiredTeacherNameOrRoomName;
                        bparser.Move();
                    }
                }
                // First name
                else if (bparser.Current == '.')
                {
                    bparser.Move();

                    var firstName = c.Parser.SourceUntilExclusive(bparser);
                    teacher.ShortFirstName = firstName;
                    c.State.Step = ParsingStep.TeacherLastName;
                }

                c.Parser.MoveTo(bparser.Position);
                break;
            }
            case ParsingStep.TeacherLastName:
            {
                var bparser = c.Parser.BufferedView();
                var skipResult = bparser.SkipUntil([' ', ',', '(']);
                if (!skipResult.SkippedAny)
                {
                    // Only the first name?
                    // This only works because it's a forward parser (no backtracking)
                    throw new WrongFormatException();
                }

                var lastName = c.Parser.SourceUntilExclusive(bparser);
                c.Parser.MoveTo(bparser.Position);
                ref var teacher = ref c.State.LastModifiers.Value.Specific.LastTeacher;
                teacher.LastName = lastName;

                if (c.Parser.IsEmpty)
                {
                    c.State.Step = ParsingStep.OptionalParensBeforeRoom;
                    break;
                }

                // Keep doing the list if found a comma.
                if (c.Parser.Current == ',')
                {
                    c.Parser.Move();
                    c.State.Step = ParsingStep.RequiredTeacherNameOrRoomName;
                    break;
                }

                c.State.Step = ParsingStep.OptionalParensBeforeRoom;
                break;
            }
            case ParsingStep.OptionalRoomName:
            {
                TryParseAndSetRoomName(c);
                AdvanceStepAfterRoom(c);
                break;
            }
        }

        static void AdvanceStepAfterRoom(ParsingContext c)
        {
            c.State.Step = ParsingStep.MaybeSubGroupAgain;
        }
        static bool TryParseAndSetRoomName(ParsingContext c)
        {
            var bparser = c.Parser.BufferedView();
            bool isRoom = c.Params.RoomParser.TryParseRoom(ref bparser);
            if (!isRoom)
            {
                Debug.Assert(bparser.Position == c.Parser.Position);
                return false;
            }

            var roomName = c.Parser.SourceUntilExclusive(bparser);
            c.Parser.MoveTo(bparser.Position);

            ref var roomNameMem = ref c.State.LastModifiers.Specific.RoomName;
            if (!roomNameMem.IsEmpty)
            {
                WrongFormatException.ThrowRoomAlreadySpecified();
            }
            roomNameMem = roomName;
            return true;
        }
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

    private struct SkipUntilDotOrSpaceOrComma : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (ch == ',')
            {
                return false;
            }
            if (ch == '.')
            {
                return false;
            }
            if (ch == ' ')
            {
                return false;
            }
            return true;
        }
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


public sealed class RoomParser
{
    public static readonly RoomParser Instance = new();

    private const string Mediacor = "Mediacor";

    public bool MightBeStart(char ch)
    {
        if (char.IsNumber(ch))
        {
            return true;
        }
        if (ch == '_')
        {
            return true;
        }
        if (ch == Mediacor[0])
        {
            return true;
        }
        return false;
    }

    public bool TryParseRoom(ref Parser parser)
    {
        if (parser.IsEmpty)
        {
            return false;
        }
        if (!MightBeStart(parser.Current))
        {
            return false;
        }
        bool ret = ParseRoom(ref parser);
        return ret;
    }

    public bool ParseRoom(ref Parser parser)
    {
        Debug.Assert(!parser.IsEmpty);
        Debug.Assert(MightBeStart(parser.Current));

        if (parser.Current == Mediacor[0])
        {
            var r = ParseMediacorRoom(ref parser);
            switch (r)
            {
                case MediacorParseProgress.Ok:
                {
                    return true;
                }
                case MediacorParseProgress.BeforeConfirmFail:
                {
                    return false;
                }
                case MediacorParseProgress.AfterConfirmFail:
                {
                    throw new WrongFormatException();
                }
                default:
                {
                    throw Unreachable();
                }
            }
        }
        else
        {
            SkipUntilWhiteOrComma(ref parser);
            return true;
        }
    }


    private static ParserHelper.SkipResult SkipUntilWhiteOrComma(ref Parser parser)
    {
        return parser.Skip(new NotWhitespaceOrCommaSkip());
    }

    private readonly struct NotWhitespaceOrCommaSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (char.IsWhiteSpace(ch))
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

    private enum MediacorParseProgress
    {
        BeforeConfirmFail,
        AfterConfirmFail,
        Ok,
    }

    private MediacorParseProgress ParseMediacorRoom(ref Parser parser)
    {
        var bparser = parser.BufferedView();

        Debug.Assert(parser.Current == Mediacor[0]);
        bparser.Move();

        {
            var mediacorEnd = Mediacor.AsSpan()[1 ..];
            if (!bparser.ConsumeExactString(mediacorEnd))
            {
                return MediacorParseProgress.BeforeConfirmFail;
            }
        }
        bparser.SkipWhitespace();

        if (bparser.IsEmpty)
        {
            return MediacorParseProgress.AfterConfirmFail;
        }

        if (bparser.Current != ',')
        {
            return MediacorParseProgress.AfterConfirmFail;
        }
        bparser.Move();
        if (bparser.IsEmpty)
        {
            return MediacorParseProgress.AfterConfirmFail;
        }

        bparser.SkipWhitespace();

        if (!bparser.ConsumeExactString("etajul"))
        {
            return MediacorParseProgress.AfterConfirmFail;
        }

        bparser.SkipWhitespace();

        if (bparser.IsEmpty)
        {
            return MediacorParseProgress.AfterConfirmFail;
        }

        {
            var bparser1 = bparser.BufferedView();

            // Maybe limit the max count to skip?
            var res = SkipUntilWhiteOrComma(ref bparser1);
            if (!res.SkippedAny)
            {
                Debug.Assert(bparser1.Current == ',');
                return MediacorParseProgress.AfterConfirmFail;
            }

            var numberSpan = bparser.PeekSpanUntilPosition(bparser1.Position);
            var romanResult = NumberHelper.FromRoman(numberSpan);
            if (romanResult is null)
            {
                return MediacorParseProgress.AfterConfirmFail;
            }

            bparser.MoveTo(bparser1.Position);
        }

        parser.MoveTo(bparser.Position);
        return MediacorParseProgress.Ok;
    }
}

file static class LessonEnd
{
    public static SkipResult SkipUntilLessonEnd(this ref Parser parser)
    {
        var result = parser.SkipUntilSequence(Sequences);
        return new()
        {
            EndOfInput = result.EndOfInput,
            Match = result.Match,
        };
    }

    private static readonly string[] Sequences = Create();
    private static int ParenIndex => 0;
    private static int CommaIndex => 1;
    private static string[] Create()
    {
        var ret = new string[2];
        ret[ParenIndex] = "(";
        ret[CommaIndex] = ", ";
        return ret;
    }

    public readonly struct SkipResult
    {
        public required bool EndOfInput { get; init; }
        public required int Match { get; init; }
        public bool IsComma => Match == CommaIndex;
        public bool IsOpeningParen => Match == ParenIndex;
    }
}

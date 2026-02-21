// TODO: Remove the use of lists.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.Lesson.Internal;
using InvalidOperationException = System.InvalidOperationException;

namespace ScheduleLib.Parsing.Lesson;

public struct ParseLessonsParams()
{
    public required Lexer Lexer;
    public required StringBuilder StringBuilder;
    public LessonTypeParser LessonTypeParser = LessonTypeParser.Instance;
    public ParityParser ParityParser = ParityParser.Instance;
    public RoomParser RoomParser = RoomParser.Instance;
    public ProcessSpaces ProcessSpacesCourseName = c => c.DefaultOnce();
}

public struct WhiteSpaceContext
{
    public LimitedLexerScope Lexer;
    public readonly LexerPosition StartPosition;

    public WhiteSpaceContext(LimitedLexerScope lexer)
    {
        Lexer = lexer;
        StartPosition = lexer.Position;
    }

    private LexerPosition EndPosition(bool skipLaterProcessing)
    {
        if (skipLaterProcessing)
        {
            return Lexer.Position;
        }
        return StartPosition;
    }

    // TODO: refine these
    public WhiteSpaceResult DefaultOnce(bool skipLaterProcessing = false)
        => WhiteSpaceResult.Create(WhiteSpaceAction.Default, StartPosition, EndPosition(skipLaterProcessing));

    public WhiteSpaceResult DefaultAll()
        => WhiteSpaceResult.Create(WhiteSpaceAction.Default, Lexer.Position, Lexer.Position);

    public WhiteSpaceResult InsertOnce(bool skipLaterProcessing = false)
        => WhiteSpaceResult.Create(WhiteSpaceAction.Insert, StartPosition, EndPosition(skipLaterProcessing));

    public WhiteSpaceResult InsertAll()
        => WhiteSpaceResult.Create(WhiteSpaceAction.Insert, Lexer.Position, Lexer.Position);

    public WhiteSpaceResult DontInsertOnce(bool skipLaterProcessing = false)
        => WhiteSpaceResult.Create(WhiteSpaceAction.DontInsert, StartPosition, EndPosition(skipLaterProcessing));

    public WhiteSpaceResult DontInsertAll(bool inclusive = true)
        => WhiteSpaceResult.Create(WhiteSpaceAction.DontInsert, Lexer.Position, Lexer.Position, inclusive);
}
public readonly record struct WhiteSpaceResult(
    WhiteSpaceAction Action,
    LexerPosition LastAppliedPosition,
    LexerPosition LastSkippedPosition)
{
    public static WhiteSpaceResult Create(
        WhiteSpaceAction action,
        LexerPosition start,
        LexerPosition end,
        bool inclusive = true)
    {
        if (!inclusive)
        {
            start = new(start.Value - 1);
            end = new(end.Value - 1);
        }

        return new WhiteSpaceResult(
            action,
            start,
            end);
    }
}

public enum WhiteSpaceAction
{
    Default,
    Insert,
    DontInsert,
}

public delegate WhiteSpaceResult ProcessSpaces(WhiteSpaceContext context);

public struct TeacherName
{
    public NameParts<ReadOnlyMemory<char>> FirstName;
    public NameParts<ReadOnlyMemory<char>> LastName;

    public bool IsNull
    {
        get
        {
            return FirstName.All(x => x.IsEmpty)
                && LastName.All(x => x.IsEmpty);
        }
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

internal struct ParsingStateStack
{
    private ListWithCapacity<ParsingState> _list;

    public ParsingStateStack()
    {
        _list = new();
        _list.Add();
    }

    public int Count => _list.Count;
    public ref ParsingState First() => ref _list.Items[0];
    public ref ParsingState Last() => ref _list.Items[^1];

    public ref ParsingState Push()
    {
        ref var newState = ref _list.Add();
        _list.Items[_list.Count - 2].CopyInto(ref newState);
        return ref newState;
    }

    public void Pop(bool apply)
    {
        if (_list.Count < 2)
        {
            throw new InvalidOperationException("Cannot pop the base layer.");
        }
        if (apply)
        {
            ref var x = ref _list.Items[_list.Count - 2];
            ref var y = ref _list.Items[_list.Count - 1];
            var c = x;
            x = y;
            y = c;
        }
        _list.RemoveLast();
    }
}

public static class LessonParsingHelper
{
    private static readonly TokenTypeLabels _labels =
        LexerHelper.CreateLabels(typeof(LessonTokenType));

    public static Lexer CreateLexer()
    {
        return new(LessonTokenReader.Instance, _labels);
    }

    public static IEnumerable<ParsedLesson> ParseLessons(ParseLessonsParams p)
    {
        ParsingStateStack stateStack = new();
        var lexer = p.Lexer;

        while (lexer.CanPeek())
        {
            if (lexer.TryConsume(TokenType.EndOfStream))
            {
                break;
            }

            var lexerScope = lexer.Scope();
            var context = new ParsingContext
            {
                Params = ref p,
                Lexer = ref lexerScope,
                StateStack = ref stateStack,
            };
            DoParsingIterWrapped(context);
            lexerScope.Apply();

            if (context.State.Step == ParsingStep.Output)
            {
                foreach (var x in DoOutput())
                {
                    yield return x;
                }
            }
        }

        {
            ref var state = ref stateStack.First();
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
        }

        IEnumerable<ParsedLesson> DoOutput()
        {
            ref var state = ref stateStack.First();
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

            var lessonsE = state.LessonsInParsing.GetEnumerator();
            while (lessonsE.MoveNext())
            {
                var allFallback = allDefaults;

                ref var lesson = ref lessonsE.Current;
                var defaultKeyIndex = lesson.Modifiers.FindIndex(SubLessonModifiersKey.Default);
                if (defaultKeyIndex != -1)
                {
                    ref var all = ref lesson.Modifiers.Ref(defaultKeyIndex);
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
                            ref var state1 = ref stateStack.First();
                            var defaultIndex = state1.DefaultModifiers.FindIndex(mod.Key.SubGroup);
                            if (defaultIndex != -1)
                            {
                                ref var def = ref state1.DefaultModifiers.Ref(defaultIndex);
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

                        yield return Output(mod.Key.SubGroup, v, lessonsE.Current.LessonName);
                    }
                }

                ref var state2 = ref stateStack.First();
                foreach (var defaultMod in state2.DefaultModifiers)
                {
                    ref var lesson1 = ref lessonsE.Current;
                    if (defaultMod.SubGroup == SubGroup.All
                        && !lesson1.Modifiers.IsEmpty)
                    {
                        continue;
                    }

                    var key = new SubLessonModifiersKey
                    {
                        SubGroup = defaultMod.SubGroup,
                        LessonType = LessonType.Unspecified,
                    };
                    var lessonModIndex = lesson1.Modifiers.FindIndex(key);
                    if (lessonModIndex != -1)
                    {
                        continue;
                    }

                    var v = allFallback;
                    v.General.UpdateIfNotDefault(defaultMod.General);
                    v.Specific.UpdateIfNotDefault(defaultMod.Specific);

                    yield return Output(defaultMod.SubGroup, v, lesson1.LessonName);
                }

                {
                    ref var lesson1 = ref lessonsE.Current;
                    ref var state3 = ref stateStack.First();

                    if (lesson1.Modifiers.IsEmpty
                        && state3.DefaultModifiers.IsEmpty)
                    {
                        yield return Output(
                            SubGroup.All,
                            allFallback,
                            lesson1.LessonName);
                    }
                }
            }

            ParsedLesson Output(
                SubGroup subGroup,
                in DefaultModifiersValue v,
                ReadOnlyMemory<char> lessonName)
            {
                var l = new List<TeacherName>(v.Specific.TeacherNames.Count);
                foreach (var t in v.Specific.TeacherNames)
                {
                    l.Add(t.Name);
                }
                ref var state = ref stateStack.First();
                return new()
                {
                    LessonName = lessonName,
                    StartTime = state.CommonLesson.StartTime,
                    LessonType = v.General.LessonType,
                    Parity = v.General.Parity,
                    SubGroup = subGroup,
                    GroupName = v.General.GroupName,
                    TeacherNames = l,
                    RoomName = v.Specific.RoomName,
                };
            }
        }
    }

    private static bool TryParsingUntilOutputOrTerminalState(ParsingContext c)
    {
        var lexerCopy = c.LexerCopy;
        while (true)
        {
            try
            {
                DoParsingIterWrapped(c with
                {
                    Lexer = ref lexerCopy,
                });
            }
            catch (WrongFormatException e)
            {
                _ = e;
                break;
            }

            void Apply(ref ParsingContext c1)
            {
                c1.Lexer.MoveTo(lexerCopy.Position);
            }
            if (c.State.Step == ParsingStep.Output)
            {
                Apply(ref c);
                return true;
            }
            if (lexerCopy.IsEmpty && c.State.IsTerminalState)
            {
                Apply(ref c);
                return true;
            }
            if (lexerCopy.IsEmpty)
            {
                break;
            }
        }

        return false;
    }

    private static void DoParsingIterWrapped(ParsingContext c)
    {
        while (c.Lexer.TryConsumeAny([
                   TokenType.EndOfLine,
                   TokenType.Whitespace]))
        {
        }
        if (c.Lexer.IsEmpty)
        {
            return;
        }
        ref var state = ref c.StateStack.Last();
        if (state.Step == ParsingStep.Output)
        {
            state.Reset();
        }

        int countBefore = c.StateStack.Count;
        var stepBefore = state.Step;
        var posBefore = c.Lexer.Position;

        DoParsingIter(c);

        if (countBefore != c.StateStack.Count)
        {
            throw new InvalidOperationException("Stack frame pushed and not popped.");
        }
        if (stepBefore == state.Step
            && c.Lexer.Position == posBefore)
        {
            throw new InvalidOperationException("Infinite loop in the parser");
        }
    }

    private static void DoParsingIter(ParsingContext c)
    {
        switch (c.State.Step)
        {
            case ParsingStep.TimeOverride:
            case ParsingStep.Start:
            {
                if (ParseTime(ref c.Lexer) is { } time)
                {
                    c.State.CommonLesson.StartTime = time;
                }

                c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                break;
            }
            case ParsingStep.OptionalStarBeforeLessonName:
            {
                if (c.Lexer.TryConsume('*'))
                {
                    c.State.CommonLesson.HasStar = true;
                }
                c.State.Step = ParsingStep.OptionalSubGroupBeforeLessonName;
                break;
            }
            case ParsingStep.LessonName:
            {
                var endPosition = FindPositionOfLastModifierGroup(c.Lexer);
                var name = CleanName(
                    c.Params.ProcessSpacesCourseName,
                    c.Lexer.Until(endPosition),
                    c.Params.StringBuilder);
                if (name.Length == 0)
                {
                    WrongFormatException.ThrowEmptyCourseName();
                }
                c.State.LessonsInParsing.Add().LessonName = name.AsMemory();
                c.State.Step = ParsingStep.OptionalParens;
                c.Lexer.MoveTo(endPosition);
                break;
            }
            case ParsingStep.OptionalParens:
            case ParsingStep.OptionalParensBeforeRoom:
            {
                if (EarlyExitNextStep(c))
                {
                    break;
                }

                foreach (var l in c.Lexer.List())
                {
                    var lexer = l;

                    if (lexer.IsEmpty)
                    {
                        WrongFormatException.EmptyListItem();
                    }
                    lexer.TryConsume(TokenType.Whitespace);

                    // Check if it's the subgroup form.
                    // ROMAN-modifier
                    var key = ParseOutKey(ref lexer, c.Params.LessonTypeParser);
                    ref var modifiers = ref GetCurrentModifiers(c, key);
                    var modifierValue = ParseOutModifier(c, ref lexer, c.Params.StringBuilder);
                    bool somethingSet = modifiers.Set(modifierValue);
                    if (!somethingSet)
                    {
                        throw new WrongFormatException("Modifier group that did nothing");
                    }
                    lexer.TryConsume(TokenType.Whitespace);
                    if (!lexer.IsEmpty)
                    {
                        WrongFormatException.ExtraWordsInModifier();
                    }
                }

                break;

                static bool EarlyExitNextStep(ParsingContext c)
                {
                    if (c.Lexer.TryConsume('('))
                    {
                        return false;
                    }
                    if (c.State.Step == ParsingStep.OptionalParensBeforeRoom)
                    {
                        c.Lexer.TryConsume(',');
                        c.State.Step = ParsingStep.OptionalRoomName;
                        return true;
                    }

                    c.State.Step = ParsingStep.OptionalSubGroupBeforeTeacher;
                    if (!c.Lexer.TryConsume(','))
                    {
                        return true;
                    }

                    // Keep parsing assuming the comma isn't a lesson separator.
                    // NOTE:
                    // This is only done when there are modifiers,
                    // in order to disambiguate lessons with commas in name.
                    if (!c.State.CurrentSubLesson.Modifiers.IsEmpty)
                    {
                        c.StateStack.Push();
                        if (TryParsingUntilOutputOrTerminalState(c)
                            && !c.State.LastModifiers.Specific.LastTeacher.IsNull)
                        {
                            c.StateStack.Pop(apply: true);
                            return true;
                        }
                        c.StateStack.Pop(apply: false);
                    }

                    // Default to another lesson name.
                    c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                    return true;
                }

                static SubLessonModifiersKey ParseOutKey(
                    ref LimitedLexerScope lexer,
                    LessonTypeParser lessonTypeParser)
                {
                    if (!lexer.CanPeek(2))
                    {
                        return new();
                    }

                    var second = lexer.Peek(2);
                    if (!second.Is('-'))
                    {
                        return new();
                    }

                    var t = lexer.Peek(1);
                    if (t.Type != LessonTokenType.Word)
                    {
                        WrongFormatException.ExpectedWordToken();
                    }

                    lexer.Move(2);
                    if (lessonTypeParser.Parse(t.Value.Span) is { } lessonType)
                    {
                        return new()
                        {
                            LessonType = lessonType,
                        };
                    }

                    var subGroup = new SubGroup(t.Value.Span.ToString());
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

                static MaybeGeneralModifiersValue ParseOutModifier(
                    ParsingContext c,
                    ref LimitedLexerScope lexer,
                    StringBuilder sb)
                {
                    if (lexer.IsEmpty)
                    {
                        WrongFormatException.LexerEmpty();
                    }
                    var t = lexer.Current;
                    if (t.Type != LessonTokenType.Word)
                    {
                        WrongFormatException.InvalidToken(t);
                    }

                    if (c.Params.LessonTypeParser.Parse(t.Value.Span) is { } lessonType)
                    {
                        lexer.Move();
                        return new()
                        {
                            LessonType = lessonType,
                        };
                    }
                    if (c.Params.ParityParser.Parse(t.Value.Span) is { } parity1)
                    {
                        lexer.Move();
                        return new()
                        {
                            Parity = parity1,
                        };
                    }

                    var groupName = lexer.Concat(new()
                    {
                        StringBuilder = sb,
                        ConcattedType = LessonTokenType.Word,
                        WhitespaceReplacer = " ",
                    });
                    Debug.Assert(!groupName.IsEmpty);
                    return new()
                    {
                        GroupName = groupName,
                    };
                }
            }
            case ParsingStep.OptionalSubGroupBeforeLessonName:
            case ParsingStep.OptionalSubGroupBeforeTeacher:
            case ParsingStep.MaybeSubGroupAgain:
            {
                if (c.State.Step == ParsingStep.OptionalSubGroupBeforeTeacher)
                {
                    // Lesson names might be delimited by a comma.
                    if (c.Lexer.TryConsume(','))
                    {
                        c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                        break;
                    }
                }
                else if (c.State.Step == ParsingStep.MaybeSubGroupAgain)
                {
                    // Just ignore the comma if we're here again.
                    // This handles the case where there are multiple subgroups.
                    if (c.Lexer.TryConsume(','))
                    {
                        // Skip whitespace
                        break;
                    }

                    if (c.State.SubGroupAppliedBeforeLessonName)
                    {
                        c.State.Step = ParsingStep.Output;
                        break;
                    }
                }

                var subgroupToken = c.Lexer.Current;
                var lexer = c.Lexer;
                if (!SkipSubGroup(ref lexer))
                {
                    SetNoSubgroup(c);
                    break;
                }

                if (c.State.Step == ParsingStep.OptionalSubGroupBeforeLessonName)
                {
                    c.State.SubGroupAppliedBeforeLessonName = true;
                }

                var subgroup = new SubGroup(subgroupToken.Value.Span.ToString());
                c.State.SetDefaultModifier(subgroup);

                c.Lexer.MoveTo(lexer.Position);

                c.State.Step = c.State.Step switch
                {
                    ParsingStep.OptionalSubGroupBeforeLessonName
                        => ParsingStep.LessonName,
                    ParsingStep.MaybeSubGroupAgain or ParsingStep.OptionalSubGroupBeforeTeacher
                        => ParsingStep.OptionalTeacherNameOrRoomName,
                    _ => throw Unreachable(),
                };
                break;

                static bool SkipSubGroup(ref LexerScope lexer)
                {
                    var t = lexer.Current;
                    if (t.Type != LessonTokenType.Word)
                    {
                        return false;
                    }
                    var sp = t.Value.Span;
                    if (char.IsNumber(sp[0]))
                    {
                        return false;
                    }

                    // Format S{Number}{OptionalNumber}
                    bool MatchS(ReadOnlyMemory<char> p)
                    {
                        var parser = new Parser(p);
                        Debug.Assert(!parser.IsEmpty);
                        if (!parser.ConsumeExactChar('S'))
                        {
                            return false;
                        }
                        if (parser.ConsumePositiveIntWithMaxLength(maxLength: 2) == null)
                        {
                            return false;
                        }
                        if (!parser.IsEmpty)
                        {
                            return false;
                        }
                        return true;
                    }
                    if (MatchS(lexer.Current.Value))
                    {
                        lexer.Move();
                        return true;
                    }

                    if (VerifyColonOrDot(ref lexer))
                    {
                        return true;
                    }
                    return false;
                }

                static bool VerifyColonOrDot(ref LexerScope lexer)
                {
                    lexer.Move();
                    if (lexer.IsEmpty)
                    {
                        return false;
                    }
                    if (!lexer.TryConsumeAny(":."))
                    {
                        return false;
                    }
                    if (!lexer.TryConsume(TokenType.Whitespace))
                    {
                        return false;
                    }
                    return true;
                }

                static void SetNoSubgroup(ParsingContext c)
                {
                    if (c.State.Step == ParsingStep.MaybeSubGroupAgain)
                    {
                        c.State.Step = ParsingStep.Output;
                        return;
                    }
                    if (c.State.Step == ParsingStep.OptionalSubGroupBeforeLessonName)
                    {
                        c.State.Step = ParsingStep.LessonName;
                        return;
                    }
                    // Maybe should check how it was added and give an error if it was
                    // added through "subgroup:" rather than "subgroup-modifier" syntax.
                    c.State.SetDefaultModifier(SubGroup.All);
                    c.State.Step = ParsingStep.OptionalTeacherNameOrRoomName;
                }
            }
            case ParsingStep.RequiredTeacherNameOrRoomName:
            case ParsingStep.OptionalTeacherNameOrRoomName:
            case ParsingStep.OptionalFullTeacherNameOrRoomNameOrSubGroup:
            {
                if (TryParseAndSetRoomName(c))
                {
                    c.Lexer.TryConsume(TokenType.Whitespace);
                    if (c.Lexer.IsEmpty)
                    {
                        AdvanceStepAfterRoom(c);
                        break;
                    }
                    if (c.Lexer.TryConsume(','))
                    {
                        // Continue the list.
                        c.State.Step = ParsingStep.RequiredTeacherNameOrRoomName;
                        break;
                    }

                    AdvanceStepAfterRoom(c);
                    break;
                }

                var lexer = c.Lexer;
                bool success = Teacher(
                    c,
                    ref lexer,
                    onlyAllowFullForm: c.State.Step == ParsingStep.OptionalFullTeacherNameOrRoomNameOrSubGroup);
                if (success)
                {
                    c.State.Step = NextStep(ref lexer);
                    c.Lexer.MoveTo(lexer.Position);

                    static ParsingStep NextStep(ref LexerScope lexer)
                    {
                        if (lexer.TryConsume(TokenType.EndOfLine))
                        {
                            return ParsingStep.OptionalRoomName;
                        }
                        ParsingStep? ret = null;
                        if (lexer.TryConsume(TokenType.Whitespace))
                        {
                            ret = ParsingStep.OptionalParensBeforeRoom;
                        }
                        if (lexer.TryConsume(','))
                        {
                            ret = ParsingStep.RequiredTeacherNameOrRoomName;
                        }
                        if (ret is not { } step)
                        {
                            WrongFormatException.InvalidToken(lexer.Current);
                            return default;
                        }
                        return step;
                    }
                }
                else
                {
                    if (c.State.Step == ParsingStep.RequiredTeacherNameOrRoomName)
                    {
                        throw new WrongFormatException("Required teacher name after comma");
                    }

                    // We've already tried for room name.
                    AdvanceStepAfterRoom(c);
                }
                break;

                static bool Teacher(
                    ParsingContext c,
                    ref LexerScope lexer,
                    bool onlyAllowFullForm)
                {
                    TeacherName result = new();
                    if (!ParseTeacherName(
                            c,
                            ref result,
                            ref lexer,
                            onlyAllowFullForm))
                    {
                        return false;
                    }

                    ValidateNotShort(result.LastName);
                    MakeAllShortIfOneShort(ref result.FirstName);

                    {
                        var copy = lexer;
                        copy.TryConsume(TokenType.Whitespace);
                        if (copy.TryConsume(':'))
                        {
                            return false;
                        }
                    }

                    if (c.State.LastModiferIndex == -1)
                    {
                        c.State.SetDefaultModifier(SubGroup.All);
                    }
                    ref var teacher = ref c.State.LastModifiers.Specific.NewTeacher();
                    teacher = result;
                    return true;
                }

                static void ValidateNotShort(NameParts<ReadOnlyMemory<char>> name)
                {
                    if (name.Any(x => !x.IsEmpty && x.Span[^1] == WordHelper.ShortenedWordCharacter))
                    {
                        WrongFormatException.InvalidLastName();
                    }
                }

                static void MakeAllShortIfOneShort(ref NameParts<ReadOnlyMemory<char>> name)
                {
                    bool oneIsShort = name.Any(x =>
                    {
                        if (x.IsEmpty)
                        {
                            return false;
                        }
                        var w = new WordSpan(x.Span);
                        if (w.LooksFull)
                        {
                            return false;
                        }
                        return true;
                    });
                    if (oneIsShort)
                    {
                        foreach (ref var x in name)
                        {
                            if (x.IsEmpty)
                            {
                                break;
                            }
                            var w = new WordSpan(x.Span);
                            if (w.LooksFull)
                            {
                                x = $"{x.Span}{WordHelper.ShortenedWordCharacter}".AsMemory();
                            }
                        }
                    }
                }

                static bool ParseTeacherName(
                    ParsingContext c,
                    ref TeacherName res,
                    ref LexerScope lexer,
                    bool onlyAllowFullForm)
                {
                    var copy = lexer;

                    var name1 = Name(ref copy);
                    if (name1 == default)
                    {
                        return false;
                    }
                    if (onlyAllowFullForm)
                    {
                        var n = name1.LastNotEmpty();
                        if (n.Length > 3)
                        {
                            return false;
                        }
                        var w = new WordSpan(n.Span);
                        if (w.LooksFull)
                        {
                            return false;
                        }
                    }

                    bool FullFormReturn(ref LexerScope lexer)
                    {
                        if (onlyAllowFullForm)
                        {
                            return false;
                        }
                        lexer.MoveTo(copy.Position);
                        return true;
                    }

                    res.LastName = name1;

                    if (WhitespaceHandling_IsDone(c, ref copy))
                    {
                        return FullFormReturn(ref lexer);
                    }

                    var name2 = Name(ref copy);
                    if (name2 == default)
                    {
                        return FullFormReturn(ref lexer);
                    }

                    if (onlyAllowFullForm)
                    {
                        if (name2[0].Span[^1] == WordHelper.ShortenedWordCharacter)
                        {
                            return false;
                        }
                    }

                    lexer.MoveTo(copy.Position);
                    res.FirstName = name1;
                    res.LastName = name2;
                    return true;

                    static bool WhitespaceHandling_IsDone(ParsingContext c, ref LexerScope lexer)
                    {
                        if (lexer.IsEmpty)
                        {
                            return true;
                        }
                        var t = lexer.Current;
                        if (t.Type != TokenType.Whitespace)
                        {
                            return false;
                        }

                        var copy = lexer;
                        // Skip whitespace
                        copy.Move();
                        // If the next token is a room, it's not last name.
                        if (c.Params.RoomParser.TryParseRoom(ref copy))
                        {
                            return true;
                        }

                        // Apply skip whitespace
                        lexer.MoveTo(copy.Position);

                        return false;
                    }
                }
            }
            case ParsingStep.OptionalRoomName:
            {
                TryParseAndSetRoomName(c);
                AdvanceStepAfterRoom(c);
                break;
            }
        }

        static string CleanName(
            ProcessSpaces processSpaces,
            LimitedLexerScope lexer,
            StringBuilder sb)
        {
            // Just a dummy by default, so it doesn't break on check.
            var whiteSpaceResult = WhiteSpaceResult.Create(
                WhiteSpaceAction.Default,
                lexer.Position,
                lexer.Position,
                inclusive: false);
            bool defaultShouldAppendWhitespace = false;
            bool shouldAppendWhitespace = false;

            try
            {
                while (!lexer.IsEmpty)
                {
                    if (ShouldRefreshWhiteSpace())
                    {
                        whiteSpaceResult = processSpaces(new(lexer));
                    }
                    if (!ProcessCurrentToken())
                    {
                        return "";
                    }
                    UpdateManualWhitespace();
                    lexer.Move();
                }
            }
            catch
            {
                sb.Clear();
                throw;
            }
            return sb.ToStringAndClear();

            bool ProcessCurrentToken()
            {
                var t = lexer.Current;
                if (t.Type == TokenType.Invalid)
                {
                    WrongFormatException.InvalidToken(t);
                    return false;
                }
                if (t.IsAnyWord())
                {
                    AppendCurrentWord();
                    if (t.Type == LessonTokenType.ShortWord)
                    {
                        defaultShouldAppendWhitespace = true;
                    }
                    return true;
                }
                // The only allowed separators
                if (t.Is(',') || t.Is(')'))
                {
                    AppendCurrentWord();
                    defaultShouldAppendWhitespace = true;
                    return true;
                }
                if (t.Is('('))
                {
                    AppendCurrentWord();
                    return true;
                }
                if (t.Is('-'))
                {
                    AppendCurrentWord();
                    return true;
                }
                if (t.Type == TokenType.Whitespace)
                {
                    defaultShouldAppendWhitespace = true;
                    return true;
                }

                WrongFormatException.InvalidToken(t);
                return false;
            }

            void UpdateManualWhitespace()
            {
                if (IsPositionApplied())
                {
                    switch (whiteSpaceResult.Action)
                    {
                        case WhiteSpaceAction.Default:
                        {
                            shouldAppendWhitespace = defaultShouldAppendWhitespace;
                            return;
                        }
                        case WhiteSpaceAction.Insert:
                        {
                            shouldAppendWhitespace = true;
                            return;
                        }
                        case WhiteSpaceAction.DontInsert:
                        {
                            shouldAppendWhitespace = false;
                            return;
                        }
                    }
                }

                shouldAppendWhitespace = defaultShouldAppendWhitespace;
            }

            void AppendCurrentWord()
            {
                var word = lexer.Current.Value.Span;

                if (shouldAppendWhitespace)
                {
                    sb.Append(' ');
                }
                shouldAppendWhitespace = false;
                defaultShouldAppendWhitespace = false;
                sb.Append(word);
            }

            bool ShouldRefreshWhiteSpace()
            {
                return lexer.Position.Value > whiteSpaceResult.LastSkippedPosition.Value;
            }
            bool IsPositionApplied()
            {
                return lexer.Position.Value <= whiteSpaceResult.LastAppliedPosition.Value;
            }
        }

        static LexerPosition FindPositionOfLastModifierGroup(LexerScope lexer)
        {
            var startPosition = lexer.Position;
            var endPosition = lexer.Position;
            bool isInsideParen = false;

            while (true)
            {
                if (lexer.IsEmpty)
                {
                    break;
                }
                var t = lexer.Current;
                if (t.Type == TokenType.EndOfLine)
                {
                    break;
                }
                if (t.Type == LessonTokenType.Word
                    && t.Value.Length >= 2
                    && !isInsideParen)
                {
                    if (ParserHelper.All(t.Value.Span[.. 2], char.IsNumber))
                    {
                        break;
                    }
                }
                else if (t.Is(',') && !isInsideParen)
                {
                    if (lexer.CanPeek(2) && lexer.Peek(2).Type == TokenType.Whitespace)
                    {
                        break;
                    }
                }
                else if (t.Is('('))
                {
                    endPosition = lexer.Position;
                    if (isInsideParen)
                    {
                        WrongFormatException.NestedParenInLessonName();
                    }
                    isInsideParen = true;
                }
                else if (t.Is(')'))
                {
                    isInsideParen = false;
                }

                lexer.Move();
            }

            if (isInsideParen)
            {
                WrongFormatException.ThrowUnclosedParenInLessonName();
            }
            if (startPosition == endPosition)
            {
                endPosition = lexer.Position;
            }
            return endPosition;
        }

        static NameParts<ReadOnlyMemory<char>> Name(ref LexerScope lexer)
        {
            var ret = default(NameParts<ReadOnlyMemory<char>>);
            if (lexer.IsEmpty)
            {
                return ret;
            }

            {
                var c = lexer.Current;
                if (!c.IsAnyWord())
                {
                    return ret;
                }
                if (!ParserHelper.All(c.Value.Span, IsTeacherNameChar))
                {
                    return ret;
                }
                ret[0] = lexer.Current.Value;
                lexer.Move();
            }

            if (lexer.IsEmpty)
            {
                return ret;
            }

            if (!lexer.TryConsume(NameConstants.DoubleNameSeparatorChar))
            {
                return ret;
            }

            {
                var c = lexer.Current;
                if (lexer.IsEmpty
                    || !c.IsAnyWord())
                {
                    WrongFormatException.ExpectedWordToken();
                }
                if (!ParserHelper.All(c.Value.Span, IsTeacherNameChar))
                {
                    WrongFormatException.InvalidCharactersInTeacherName();
                }
                ret[1] = lexer.Current.Value;
                lexer.Move();
            }

            return ret;
        }

        static void AdvanceStepAfterRoom(ParsingContext c)
        {
            c.State.Step = ParsingStep.MaybeSubGroupAgain;
        }
        static bool TryParseAndSetRoomName(ParsingContext c)
        {
            var lexer = c.Lexer;
            bool isRoom = c.Params.RoomParser.TryParseRoom(ref lexer);
            if (!isRoom)
            {
                return false;
            }

            var roomName = CleanName(
                c => c.DefaultAll(),
                c.Lexer.Until(lexer.Position),
                c.Params.StringBuilder);
            c.Lexer.MoveTo(lexer.Position);

            ref var roomNameMem = ref c.State.LastModifiers.Specific.RoomName;
            if (!roomNameMem.IsEmpty)
            {
                WrongFormatException.ThrowRoomAlreadySpecified();
            }
            roomNameMem = roomName.AsMemory();
            return true;
        }
    }

    static TimeOnly? ParseTime(ref LexerScope lexer)
    {
        var copy = lexer;
        if (copy.IsEmpty)
        {
            return null;
        }
        if (TimePart(ref copy) is not { } hours)
        {
            return null;
        }
        if (copy.IsEmpty)
        {
            return null;
        }
        if (!copy.TryConsume(':'))
        {
            return null;
        }
        if (TimePart(ref copy) is not { } mins)
        {
            return null;
        }
        lexer.MoveTo(copy.Position);
        return new TimeOnly(
            hour: (int) hours,
            minute: (int) mins);

        static uint? TimePart(ref LexerScope lexer)
        {
            var t = lexer.Current;
            if (t.Type != LessonTokenType.Word)
            {
                return null;
            }
            var span = t.Value.Span;
            if (span.Length > 2)
            {
                return null;
            }
            if (uint.TryParse(span, NumberStyles.None, provider: null, out uint result))
            {
                lexer.Move();
                return result;
            }
            return null;
        }
    }

    private static bool IsTeacherSeparator(char ch)
    {
        if (ch == ',')
        {
            return true;
        }
        if (ch == '.')
        {
            return true;
        }
        if (ch == ' ')
        {
            return true;
        }
        return false;
    }
    private static bool IsTeacherNameChar(char ch)
    {
        if (ch is '.')
        {
            return true;
        }
        if (char.IsLetter(ch))
        {
            return true;
        }
        return false;
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

    // This is supposed to be more generic than just all possible values,
    // hence why it's called "Examples"
    public ImmutableArray<string> AllowedValuesExamples => LessonTypeConstants.Names;
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

    public bool MightBeStart(Token token)
    {
        if (token.Type != LessonTokenType.Word)
        {
            return false;
        }
        var ch = token.Value.Span[0];
        if (char.IsNumber(ch))
        {
            return true;
        }
        if (ch == '_')
        {
            return true;
        }
        if (token.Value.Span[0] == Mediacor[0])
        {
            return true;
        }
        return false;
    }

    public bool TryParseRoom(ref LexerScope lexer)
    {
        if (lexer.IsEmpty)
        {
            return false;
        }
        if (!MightBeStart(lexer.Current))
        {
            return false;
        }
        bool ret = ParseRoom(ref lexer);
        return ret;
    }

    public bool ParseRoom(ref LexerScope lexer)
    {
        Debug.Assert(!lexer.IsEmpty);
        Debug.Assert(MightBeStart(lexer.Current));

        if (lexer.Current.Value.Span[0] == Mediacor[0])
        {
            var r = ParseMediacorRoom(ref lexer);
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
            {
                var t = lexer.Current;
                if (t.Type != LessonTokenType.Word)
                {
                    return false;
                }
                lexer.Move();
            }
            {
                if (lexer.IsEmpty)
                {
                    return true;
                }
                var t = lexer.Current;
                if (t.Is(',')
                    || t.Type == TokenType.Whitespace
                    || t.Type == TokenType.EndOfLine)
                {
                    return true;
                }
            }
            return false;
        }
    }

    private enum MediacorParseProgress
    {
        BeforeConfirmFail,
        AfterConfirmFail,
        Ok,
    }

    private MediacorParseProgress ParseMediacorRoom(ref LexerScope lexer)
    {
        Debug.Assert(lexer.Current.Value.Span[0] == Mediacor[0]);
        {
            var mediacorEnd = Mediacor.AsSpan()[1 ..];
            if (!lexer.Current.Value.Span[1 ..].SequenceEqual(mediacorEnd))
            {
                return MediacorParseProgress.BeforeConfirmFail;
            }
        }
        lexer.Move();
        if (lexer.IsEmpty)
        {
            return MediacorParseProgress.AfterConfirmFail;
        }
        lexer.TryConsume(TokenType.Whitespace);
        if (!lexer.TryConsume(','))
        {
            return MediacorParseProgress.AfterConfirmFail;
        }
        lexer.TryConsume(TokenType.Whitespace);
        if (!lexer.ConsumeExactWord("etajul"))
        {
            return MediacorParseProgress.AfterConfirmFail;
        }

        if (lexer.IsEmpty)
        {
            return MediacorParseProgress.AfterConfirmFail;
        }
        lexer.TryConsume(TokenType.Whitespace);

        {
            var t = lexer.Current;
            if (t.Type != LessonTokenType.Word)
            {
                return MediacorParseProgress.AfterConfirmFail;
            }

            var romanResult = NumberHelper.FromRoman(t.Value.Span);
            if (romanResult is null)
            {
                return MediacorParseProgress.AfterConfirmFail;
            }

            lexer.Move();
        }
        return MediacorParseProgress.Ok;
    }
}

internal struct DefaultModifiersList() : IBasic<DefaultModifiersList>
{
    private ListWithCapacity<DefaultModifiers> _list = new();

    public ListWithCapacity<DefaultModifiers>.Enumerator GetEnumerator() => _list.GetEnumerator();
    public bool IsEmpty => _list.Count == 0;

    public void Clear()
    {
        _list.Clear();
    }

    public ref DefaultModifiers Ref(int index)
    {
        return ref _list.Items[index];
    }

    public bool HasOtherThanAllSubGroup()
    {
        if (_list.Count == 0)
        {
            return false;
        }
        if (_list.Count != 1)
        {
            return true;
        }
        return Ref(0).SubGroup != SubGroup.All;
    }

    public int FindIndex(SubGroup subGroup)
    {
        var mods = _list.Items;
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

        ref var t = ref _list.Add();
        t.SubGroup = subGroup;

        return _list.Count - 1;
    }

    public void CopyInto(ref DefaultModifiersList other)
    {
        _list.CopyInto(ref other._list);
    }
}

internal struct SubLessonModifiersList() : IBasic<SubLessonModifiersList>
{
    private ListWithCapacity<SubLessonModifiers> _list = new();

    public ListWithCapacity<SubLessonModifiers>.Enumerator GetEnumerator() => _list.GetEnumerator();

    public bool IsEmpty => _list.Count == 0;

    public ref SubLessonModifiers Ref(int index)
    {
        return ref _list.Items[index];
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
        var mods = _list.Items;
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

        _list.Add() = new SubLessonModifiers
        {
            Key = key,
        };
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

    public void CopyInto(ref SubLessonModifiersList other)
    {
        _list.CopyInto(ref other._list);
    }

    public void Clear()
    {
        _list.Clear();
    }
}

internal struct SubLessonInParsing() : IBasic<SubLessonInParsing>
{
    public ReadOnlyMemory<char> LessonName = default;
    public SubLessonModifiersList Modifiers = new();

    public void CopyInto(ref SubLessonInParsing other)
    {
        other.LessonName = LessonName;
        Modifiers.CopyInto(ref other.Modifiers);
    }

    public void Clear()
    {
        LessonName = default;
        Modifiers.Clear();
    }
}

internal struct GeneralModifiersValue()
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

internal struct TeacherNameWrapper : IBasic<TeacherNameWrapper>
{
    public TeacherName Name;

    public void CopyInto(ref TeacherNameWrapper other)
    {
        other.Name = Name;
    }

    public void Clear()
    {
        Name = default;
    }
}

internal struct SpecificModifiersValue() : IBasic<SpecificModifiersValue>
{
    public ListWithCapacity<TeacherNameWrapper> TeacherNames = new();
    public ReadOnlyMemory<char> RoomName = default;

    public readonly ref TeacherName LastTeacher
    {
        get
        {
            return ref TeacherNames.Items[^1].Name;
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

    public ref TeacherName NewTeacher()
    {
        ref var ret = ref TeacherNames.Add();
        return ref ret.Name;
    }

    public void CopyInto(ref SpecificModifiersValue other)
    {
        other.RoomName = RoomName;
        TeacherNames.CopyInto(ref other.TeacherNames);
    }

    public void Clear()
    {
        RoomName = default;
        TeacherNames.Clear();
    }
}

internal struct DefaultModifiersValue() : IBasic<DefaultModifiersValue>
{
    public GeneralModifiersValue General = new();
    public SpecificModifiersValue Specific = new();

    public void CopyInto(ref DefaultModifiersValue other)
    {
        other.General = General;
        other.Specific = Specific;
    }

    public void Clear()
    {
        General = new();
        Specific.Clear();
    }
}

internal struct DefaultModifiers() : IBasic<DefaultModifiers>
{
    public DefaultModifiersValue Value = new();
    public SubGroup SubGroup { get; set; }

    [UnscopedRef] public ref GeneralModifiersValue General => ref Value.General;
    [UnscopedRef] public ref SpecificModifiersValue Specific => ref Value.Specific;

    public void CopyInto(ref DefaultModifiers other)
    {
        Value.CopyInto(ref other.Value);
        other.SubGroup = SubGroup;
    }

    public void Clear()
    {
        Value.Clear();
        SubGroup = SubGroup.All;
    }
}

internal readonly record struct SubLessonModifiersKey()
{
    public static SubLessonModifiersKey Default => new();
    public SubGroup SubGroup { get; init; } = SubGroup.All;
    public LessonType LessonType { get; init; } = LessonType.Unspecified;
}

internal struct SubLessonModifiers() : IBasic<SubLessonModifiers>
{
    public GeneralModifiersValue General = new();
    public SubLessonModifiersKey Key { get; set; }

    public void CopyInto(ref SubLessonModifiers other) => other = this;
    public void Clear() => this = new();
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

internal enum ParsingStep
{
    Start,
    TimeOverride,

    // Star is used for notes.
    OptionalStarBeforeLessonName,

    // May appear.
    OptionalSubGroupBeforeLessonName,
    LessonName,

    // Lesson modifiers.
    OptionalParens,

    // Subgroup may be specified before the teacher-room pair.
    OptionalSubGroupBeforeTeacher,
    // May be repeated with more teacher-room pairs.
    MaybeSubGroupAgain,

    // Rooms generally begin with a number.
    RequiredTeacherNameOrRoomName,
    OptionalTeacherNameOrRoomName,

    // NOTE:
    // For trying to see if it's a teacher or not using recursion.
    // This will fail for teachers that don't have the name yet.
    // Currently doing it this way, because I don't have
    // a full list of teachers to do a context-sensitive grammar
    OptionalFullTeacherNameOrRoomNameOrSubGroup,

    // Room modifiers.
    OptionalParensBeforeRoom,
    // Only room allowed after room modifiers.
    OptionalRoomName,

    Output,
}

internal interface IBasic<T>
{
    void CopyInto(ref T other);
    void Clear();
}

internal struct ListWithCapacity<T>() : IBasic<ListWithCapacity<T>>
    where T : IBasic<T>, new()
{
    private readonly List<T> _items = new();
    private int _count = 0;

    public void Clear()
    {
        _count = 0;
    }

    public readonly Span<T> Items => CollectionsMarshal.AsSpan(_items)[.. _count];

    public int Count => _count;

    public ref T Add()
    {
        Debug.Assert(_items.Count >= _count);
        if (_items.Count == _count)
        {
            CollectionsMarshal.SetCount(_items, _count + 1);
            _items[_count] = new();
        }
        else
        {
            ref var i = ref CollectionsMarshal.AsSpan(_items)[_count];
            i.Clear();
        }
        _count++;
        ref var ret = ref Items[_count - 1];
        return ref ret!;
    }

    public void CopyInto(ref ListWithCapacity<T> other)
    {
        int count = Math.Max(_count, other.Count);
        var countBefore = other.Count;

        other._count = _count;

        CollectionsMarshal.SetCount(other._items, count);
        var fromSpan = Items;
        var toSpan = other.Items;
        // for (int i = 0; i < countBefore; i++)
        // {
        //     toSpan[i].Clear();
        // }
        for (int i = countBefore; i < count; i++)
        {
            toSpan[i] = new();
        }
        for (int i = 0; i < _count; i++)
        {
            fromSpan[i].CopyInto(ref toSpan[i]);
        }
    }

    public void RemoveLast()
    {
        Debug.Assert(_count > 0);
        _count--;
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly ListWithCapacity<T> _self;
        private int _i;

        public Enumerator(ListWithCapacity<T> self)
        {
            _self = self;
            _i = -1;
        }

        public bool MoveNext()
        {
            _i++;
            if (_i >= _self._count)
            {
                return false;
            }
            return true;
        }

        public ref T Current => ref _self.Items[_i];
    }
}

internal struct ParsingState() : IBasic<ParsingState>
{
    public ParsingStep Step = ParsingStep.Start;
    public CommonLessonInParsing CommonLesson = new();
    public DefaultModifiersList DefaultModifiers = new();
    public ListWithCapacity<SubLessonInParsing> LessonsInParsing = new();
    public int LastModiferIndex = -1;
    public bool SubGroupAppliedBeforeLessonName;

    public ref SubLessonInParsing CurrentSubLesson => ref LessonsInParsing.Items[^1];
    public ref DefaultModifiers LastModifiers => ref DefaultModifiers.Ref(LastModiferIndex);

    public void CopyInto(ref ParsingState copy)
    {
        copy.CommonLesson = CommonLesson;
        copy.Step = Step;
        DefaultModifiers.CopyInto(ref copy.DefaultModifiers);
        LessonsInParsing.CopyInto(ref copy.LessonsInParsing);
        copy.LastModiferIndex = LastModiferIndex;
        copy.SubGroupAppliedBeforeLessonName = SubGroupAppliedBeforeLessonName;
    }

    public void Clear() => Reset();

    public void Reset()
    {
        Step = ParsingStep.TimeOverride;
        LessonsInParsing.Clear();
        DefaultModifiers.Clear();
        CommonLesson = new();
        LastModiferIndex = -1;
        SubGroupAppliedBeforeLessonName = false;
    }

    public bool IsTerminalState
    {
        get
        {
            return Step is ParsingStep.Output
                or ParsingStep.Start
                // In this format, the teacher name and the room are optional
                or ParsingStep.OptionalSubGroupBeforeTeacher
                or ParsingStep.OptionalParens
                or ParsingStep.OptionalParensBeforeRoom
                or ParsingStep.OptionalTeacherNameOrRoomName
                or ParsingStep.OptionalRoomName
                or ParsingStep.MaybeSubGroupAgain;
        }
    }

    public void SetDefaultModifier(SubGroup subGroup)
    {
        LastModiferIndex = DefaultModifiers.FindOrAdd(subGroup);
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
    internal static void InvalidToken(Token token) => throw new WrongFormatException($"Invalid Token `{token}`");

    [DoesNotReturn]
    internal static void LexerEmpty() => throw new WrongFormatException("Lexer is empty");

    [DoesNotReturn]
    internal static void ExtraWordsInModifier() => throw new WrongFormatException("Extra words in a modifier item");

    [DoesNotReturn]
    internal static void ExpectedWordToken() => throw new WrongFormatException("Expected name token");

    [DoesNotReturn]
    internal static void InvalidCharactersInTeacherName() => throw new WrongFormatException("Invalid characters in teacher name");

    [DoesNotReturn]
    internal static void ThrowEmptyCourseName() => throw new WrongFormatException("Empty course name");

    [DoesNotReturn]
    internal static void ThrowRoomAlreadySpecified() => throw new RoomAlreadySpecifiedException();

    [DoesNotReturn]
    internal static void ThrowUnclosedParenInLessonName() => throw new WrongFormatException("Unclosed paren in lesson name");

    [DoesNotReturn]
    internal static void NestedParenInLessonName() => throw new WrongFormatException("Nested paren in lesson name");

    [DoesNotReturn]
    internal static void EmptyListItem() => throw new WrongFormatException("Empty list item");

    [DoesNotReturn]
    internal static void UnclosedParens() => throw new WrongFormatException("Unclosed parens");

    [DoesNotReturn]
    internal static void InvalidLastName() => throw new WrongFormatException($"Last name must not be short");
}

internal ref struct ParsingContext
{
    public required ref readonly ParseLessonsParams Params;
    public ref ParsingState State => ref StateStack.Last();
    public required ref LexerScope Lexer;
    public required ref ParsingStateStack StateStack;

    internal readonly LexerScope LexerCopy => Lexer;
}

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

    private readonly LexerPosition EndPosition(bool skipLaterProcessing)
    {
        if (skipLaterProcessing)
        {
            return Lexer.Position;
        }
        return StartPosition;
    }

    // TODO: refine these
    public readonly WhiteSpaceResult DefaultOnce(bool skipLaterProcessing = false)
        => WhiteSpaceResult.Create(WhiteSpaceAction.Default, StartPosition, EndPosition(skipLaterProcessing));

    public readonly WhiteSpaceResult DefaultAll()
        => WhiteSpaceResult.Create(WhiteSpaceAction.Default, Lexer.Position, Lexer.Position);

    public readonly WhiteSpaceResult InsertOnce(bool skipLaterProcessing = false)
        => WhiteSpaceResult.Create(WhiteSpaceAction.Insert, StartPosition, EndPosition(skipLaterProcessing));

    public readonly WhiteSpaceResult InsertAll()
        => WhiteSpaceResult.Create(WhiteSpaceAction.Insert, Lexer.Position, Lexer.Position);

    public readonly WhiteSpaceResult DontInsertOnce(bool skipLaterProcessing = false)
        => WhiteSpaceResult.Create(WhiteSpaceAction.DontInsert, StartPosition, EndPosition(skipLaterProcessing));

    public readonly WhiteSpaceResult DontInsertAll(bool inclusive = true)
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
    public static Lexer CreateLexer() => new(LessonTokenReader.Instance);

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

            // ref var t1 = ref state.DefaultModifiers.AddDistinct();
            // t1.General.LessonType = LessonType.Lab;
            // t1.Specific.TeacherNames.Add().Name.LastName[0] = "Cuciuc".AsMemory();
            // t1.Specific.TeacherNames.Add().Name.FirstName[0] = "N.".AsMemory();
            // t1.Specific.RoomName = "239/4".AsMemory();

            // Discriminator properties:
            // lesson type (for each)
            // subgroup (for each, except default, when no default)
            // start time (only the one it applies to, the other taken from default)
            // parity (for each, except every week, when no default)
            // group name (copy to each from the default, unless overwritten)

            // Driving properties:
            // teachers (driven by lessons)
            // rooms (separate room is error, treated together with teachers)
            // lessons (cartesian product with following teachers, if single teacher node. If multiple, the first one following ??)

            var teacherNodes = new List<int>();
            for (int i = 0; i < state.AttributeRoots.Items.Length; i++)
            {
                ref var x = ref state.AttributeRoots.Items[i];
                if (!x.Teachers.Items.IsEmpty)
                {
                    teacherNodes.Add(i);
                }
            }

            var lessonNodes = new List<int>();
            for (int i = 0; i < state.AttributeRoots.Items.Length; i++)
            {
                ref var x = ref state.AttributeRoots.Items[i];
                if (!x.Lesson.IsEmpty)
                {
                    lessonNodes.Add(i);
                }
            }

            if (teacherNodes.Count > 1 && lessonNodes.Count > 1)

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
        if (SkipUseless_TrueIfSkippedEol(c))
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

        static bool SkipUseless_TrueIfSkippedEol(ParsingContext c)
        {
            bool eol = false;
            while (true)
            {
                bool skipped = false;
                while (c.Lexer.TryConsume(TokenType.Whitespace))
                {
                    skipped = true;
                }
                if (c.Lexer.TryConsume(TokenType.EndOfLine))
                {
                    eol = true;
                    skipped = true;
                }
                if (skipped)
                {
                    break;
                }
            }
            return eol;
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
                    c.State.AddModifier().Value.StartTime = time;
                }

                c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                break;
            }
            case ParsingStep.OptionalStarBeforeLessonName:
            {
                if (c.Lexer.TryConsume('*'))
                {
                }
                c.State.Step = ParsingStep.OptionalFrontModifierBeforeLessonName;
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

                ref var lesson = ref c.State.GetCurrentAttributeRoot().Lesson;
                if (!lesson.IsEmpty)
                {
                    WrongFormatException.ThrowLessonAlreadySpecified();
                }
                lesson = name.AsMemory();

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

                    var modifier = new Modifier();

                    // Check if it's the subgroup form.
                    // ROMAN-modifier
                    // OR
                    // sb NUMBER modifier
                    var subGroup = ShortSbModifierForm(ref lexer);

                    if (subGroup != default && !lexer.IsEmpty)
                    {
                        modifier.Key.SubGroup = subGroup;
                    }
                    else if (subGroup == default)
                    {
                        modifier.Key = ParseOutKey(ref lexer, c.Params.LessonTypeParser);
                    }

                    lexer.TryConsume(TokenType.Whitespace);
                    // The sb1 form without a modifier has been handled already.
                    modifier.Value = ParseOutModifier(c, ref lexer, c.Params.StringBuilder);

                    // Handle ambiguity
                    var ambiguousTags = EnumBitArray<ModifierValueTag>.From(
                        ModifierValueTag.LessonType,
                        ModifierValueTag.Parity,
                        ModifierValueTag.StartTime);
                    ref var modifiers = ref c.State.GetCurrentAttributeRoot().Modifiers;
                    bool hasAmbiguousModifier = CanFindDifferentModifierForSameThing(
                        modifiers.Items,
                        modifier,
                        ambiguousTags);
                    if (hasAmbiguousModifier)
                    {
                        throw new WrongFormatException("Duplicate value for attribute");
                    }

                    modifiers.Add() = modifier;

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

                    Debug.Assert(c.State.Step == ParsingStep.OptionalParens);

                    c.State.Step = ParsingStep.OptionalFrontModifierBeforeTeacher;
                    if (!c.Lexer.TryConsume(','))
                    {
                        return true;
                    }

                    bool hasModifiers = !c.State.GetCurrentAttributeRoot().Modifiers.Items.IsEmpty;
                    c.State.MustMakeNewAttributeRoot = true;

                    // Keep parsing assuming the comma isn't a lesson separator.
                    // NOTE:
                    // This is only done when there are modifiers,
                    // in order to disambiguate lessons with commas in name.
                    if (hasModifiers)
                    {
                        c.StateStack.Push();
                        if (TryParsingUntilOutputOrTerminalState(c)
                            && !c.State.GetCurrentAttributeRoot().Teachers.Items.IsEmpty)
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

                static ModifierKey ParseOutKey(
                    ref LimitedLexerScope lexer,
                    LessonTypeParser lessonTypeParser)
                {
                    if (!lexer.CanPeek(2))
                    {
                        return new();
                    }
                    if (RegularModifierForm(ref lexer) is { } regRet)
                    {
                        return regRet;
                    }
                    return new();


                    ModifierKey? RegularModifierForm(ref LimitedLexerScope lexer)
                    {
                        if (!lexer.Peek(2).Is('-'))
                        {
                            return null;
                        }

                        var token = lexer.Peek(1);
                        if (token.Type != LessonTokenType.Word)
                        {
                            WrongFormatException.ExpectedWordToken();
                        }

                        // Skip both the word and the -
                        lexer.Move(2);

                        if (lessonTypeParser.Parse(token.Value.Span) is { } lessonType)
                        {
                            return new()
                            {
                                LessonType = lessonType,
                            };
                        }

                        var subGroup = new SubGroup(token.Value.Span.ToString());
                        return new()
                        {
                            SubGroup = subGroup,
                        };
                    }
                }

                // sb.1 MODIFIER form
                static SubGroup ShortSbModifierForm(ref LimitedLexerScope lexer)
                {
                    var sbToken = lexer.Current;
                    if (!sbToken.IsAnyWord())
                    {
                        return default;
                    }
                    var word = new WordSpan(sbToken.Value.Span);
                    bool wordIsSb = word.Shortened.Value.Equals("sb", StringComparison.Ordinal);
                    if (!wordIsSb)
                    {
                        return default;
                    }

                    bool hadDot = sbToken.Type == LessonTokenType.ShortWord;
                    lexer.Move();

                    lexer.TryConsume(TokenType.Whitespace);

                    if (!hadDot)
                    {
                        lexer.TryConsume('.');
                        lexer.TryConsume(TokenType.Whitespace);
                    }

                    if (lexer.IsEmpty)
                    {
                        WrongFormatException.ExpectedArabSubGroup();
                    }

                    var tokNum = lexer.Current;
                    if (tokNum.Type != LessonTokenType.Number)
                    {
                        WrongFormatException.ExpectedArabSubGroup();
                    }
                    if (tokNum.Value.Length != 1)
                    {
                        WrongFormatException.ExpectedSubGroupNumberToBeOneDigit();
                    }
                    lexer.Move();
                    var number = tokNum.Value.Span[0] - '0';
                    var subGroup = SubGroup.CreateNumeric(number);
                    return subGroup;
                }

                static ModifierValue ParseOutModifier(
                    ParsingContext c,
                    ref LimitedLexerScope lexer,
                    StringBuilder sb)
                {
                    if (lexer.IsEmpty)
                    {
                        WrongFormatException.LexerEmpty();
                    }
                    var t = lexer.Current;
                    if (t.Type is not LessonTokenType.Word)
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

                    var groupName = lexer.ConcatWithSpaceReplacement(new()
                    {
                        StringBuilder = sb,
                        ConcattedTypes = [LessonTokenType.Word, LessonTokenType.Number],
                        WhitespaceReplacer = " ",
                    });
                    Debug.Assert(!groupName.IsEmpty);
                    return new()
                    {
                        GroupName = groupName,
                    };
                }
            }
            case ParsingStep.OptionalFrontModifierBeforeLessonName:
            case ParsingStep.OptionalFrontModifierBeforeTeacher:
            case ParsingStep.MaybeFrontModifierAgain:
            {
                if (c.State.Step == ParsingStep.OptionalFrontModifierBeforeTeacher)
                {
                    // Lesson names might be delimited by a comma.
                    if (c.Lexer.TryConsume(','))
                    {
                        c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                        break;
                    }
                }
                else if (c.State.Step == ParsingStep.MaybeFrontModifierAgain)
                {
                    // Just ignore the comma if we're here again.
                    // This handles the case where there are multiple subgroups.
                    if (c.Lexer.TryConsume(','))
                    {
                        // Skip whitespace
                        break;
                    }

                    if (c.State.FrontModifierAppliedBeforeLessonName)
                    {
                        c.State.Step = ParsingStep.Output;
                        break;
                    }
                }

                var frontMod = TryReadFrontModifier(c);
                if (frontMod.IsNull)
                {
                    SetNoFrontModifiers(c);
                    break;
                }

                c.State.AddModifier().Value = frontMod;
                if (c.State.Step == ParsingStep.OptionalFrontModifierBeforeLessonName)
                {
                    c.State.FrontModifierAppliedBeforeLessonName = true;
                }

                c.State.Step = c.State.Step switch
                {
                    ParsingStep.OptionalFrontModifierBeforeLessonName
                        => ParsingStep.LessonName,
                    ParsingStep.MaybeFrontModifierAgain or ParsingStep.OptionalFrontModifierBeforeTeacher
                        => ParsingStep.OptionalTeacherNameOrRoomName,
                    _ => throw Unreachable(),
                };
                break;

                static ModifierValue TryReadFrontModifier(ParsingContext c)
                {
                    var ret = new ModifierValue();
                    if (TryReadParity(c) is { } parity)
                    {
                        ret.Parity = parity;
                    }
                    else if (TryReadSubGroup(c) is { } subgroup)
                    {
                        ret.SubGroup = subgroup;
                    }
                    return ret;
                }

                static Parity? TryReadParity(ParsingContext c)
                {
                    var lexer = c.Lexer;

                    var t = lexer.Current;
                    if (!t.IsAnyWord())
                    {
                        return null;
                    }
                    var word = new WordSpan(t.Value.Span);
                    if (c.Params.ParityParser.Parse(word.Shortened.Value) is not { } parity)
                    {
                        return null;
                    }

                    lexer.Move();

                    // No . at the end
                    if (word.LooksFull)
                    {
                        // Only skip if no .
                        // . is one of the indicator characters.
                        if (!SkipFrontModifierIndicator(ref lexer))
                        {
                            return null;
                        }
                    }

                    c.Lexer.MoveTo(lexer.Position);
                    return parity;
                }

                static SubGroup? TryReadSubGroup(ParsingContext c)
                {
                    var subgroupStartLexer = c.Lexer;
                    var lexer = c.Lexer;
                    if (SkipSubGroup(ref lexer) is not { } subGroupEndPos)
                    {
                        return null;
                    }

                    var sb = c.Params.StringBuilder;
                    sb.Clear();
                    var subGroupStr = subgroupStartLexer
                        .Until(subGroupEndPos)
                        .Concat(sb);
                    var subgroup = new SubGroup(subGroupStr.ToString());

                    c.Lexer.MoveTo(lexer.Position);
                    return subgroup;
                }


                static LexerPosition? SkipSubGroup(ref LexerScope lexer)
                {
                    var t = lexer.Current;
                    if (t.Type != LessonTokenType.Word)
                    {
                        return null;
                    }
                    if (ConsumeS(ref lexer) is { } spos)
                    {
                        return spos;
                    }
                    if (VerifyColonOrDot(ref lexer) is { } cpos)
                    {
                        return cpos;
                    }
                    return null;

                    // Format S{Number}{OptionalNumber}
                    LexerPosition? ConsumeS(ref LexerScope lexer)
                    {
                        var lexerCopy = lexer;
                        if (!lexerCopy.ConsumeExactWord("S"))
                        {
                            return null;
                        }
                        if (lexerCopy.IsEmpty)
                        {
                            return null;
                        }

                        var t = lexerCopy.Current;
                        if (t.Type != LessonTokenType.Number)
                        {
                            return null;
                        }
                        if (t.Value.Length > 2)
                        {
                            return null;
                        }
                        lexerCopy.Move();

                        lexer.MoveTo(lexerCopy.Position);
                        return lexer.Position;
                    }
                }

                static LexerPosition? VerifyColonOrDot(ref LexerScope lexer)
                {
                    lexer.Move();

                    // TODO:
                    // This feels kinda dirty, but it would work because of the fact
                    // that spaces or separator would separate normally.
                    while (lexer.TryConsumeAny([LessonTokenType.Number, LessonTokenType.Word]))
                    {
                    }

                    if (lexer.IsEmpty)
                    {
                        return null;
                    }
                    var endPos = lexer.Position;
                    if (!SkipFrontModifierIndicator(ref lexer))
                    {
                        return null;
                    }
                    return endPos;
                }

                static bool SkipFrontModifierIndicator(ref LexerScope lexer)
                {
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

                static void SetNoFrontModifiers(ParsingContext c)
                {
                    if (c.State.Step == ParsingStep.MaybeFrontModifierAgain)
                    {
                        c.State.Step = ParsingStep.Output;
                        return;
                    }
                    if (c.State.Step == ParsingStep.OptionalFrontModifierBeforeLessonName)
                    {
                        c.State.Step = ParsingStep.LessonName;
                        return;
                    }
                    // Maybe should check how it was added and give an error if it was
                    // added through "subgroup:" rather than "subgroup-modifier" syntax.
                    // c.State.SetDefaultModifier(SubGroup.All);
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

                    ref var teacher = ref c.State.GetCurrentAttributeRoot().Teachers.Add().Name;
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
                if (t.IsAnyWord() || t.Type == LessonTokenType.Number)
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
                if (t.Is('/'))
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
                if (t.Type == LessonTokenType.Number
                    && t.Value.Length >= 2
                    && !isInsideParen)
                {
                    break;
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
            c.State.MustMakeNewAttributeRoot = true;
            c.State.Step = ParsingStep.MaybeFrontModifierAgain;
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
                c =>
                {
                    var t = c.Lexer.Current;
                    if (t.Type == LessonTokenType.Number)
                    {
                        return c.DontInsertAll();
                    }
                    if (t.Is('/'))
                    {
                        return c.DontInsertAll();
                    }
                    return c.DefaultAll();
                },
                c.Lexer.Until(lexer.Position),
                c.Params.StringBuilder);
            c.Lexer.MoveTo(lexer.Position);

            ref var roomNameMem = ref c.State.GetCurrentAttributeRoot().Room;
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
        if (!copy.TryConsumeAny(":."))
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
            if (t.Type != LessonTokenType.Number)
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

    private static int FindModifier(
        Span<Modifier> modifiers,
        ModifierKey key,
        ModifierValueTag tag)
    {
        for (int i = 0; i < modifiers.Length; i++)
        {
            ref var it = ref modifiers[i];
            if (it.Key == key && it.Value.Tag == tag)
            {
                return i;
            }
        }

        return -1;
    }


    private static bool CanFindDifferentModifierForSameThing(
        Span<Modifier> modifiers,
        in Modifier modifier,
        EnumBitArray<ModifierValueTag> comparedTags)
    {
        var x = FindDifferentModifierForSameThing(modifiers, modifier, comparedTags);
        return x.IsNull;
    }

    private static ModifierValue FindDifferentModifierForSameThing(
        Span<Modifier> modifiers,
        in Modifier modifier,
        EnumBitArray<ModifierValueTag> comparedTags)
    {
        ModifierValue Default = new();
        if (!comparedTags.Contains(modifier.Value.Tag))
        {
            return Default;
        }

        int i = FindModifier(
            modifiers,
            modifier.Key,
            modifier.Value.Tag);
        if (i < 0)
        {
            return Default;
        }

        ref var other = ref modifiers[i];
        var a = modifier.Value;
        var b = other.Value;
        if (AreEqual(a, b))
        {
            return Default;
        }
        return b;
    }

    private static bool AreEqual(this ModifierValue a, ModifierValue b)
    {
        switch (a.Tag)
        {
            case ModifierValueTag.GroupName:
            {
                if (a.GroupName.Span.Equals(b.GroupName.Span, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
            case ModifierValueTag.LessonType:
            {
                if (a.LessonType == b.LessonType)
                {
                    return true;
                }

                return false;
            }
            case ModifierValueTag.Parity:
            {
                if (a.Parity == b.Parity)
                {
                    return true;
                }

                return false;
            }
            case ModifierValueTag.StartTime:
            {
                if (a.StartTime == b.StartTime)
                {
                    return true;
                }

                return false;
            }
            case ModifierValueTag.SubGroup:
            {
                if (a.SubGroup == b.SubGroup)
                {
                    return true;
                }

                return false;
            }
            default:
            {
                throw Unreachable();
            }
        }
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
        if (token.Type == LessonTokenType.Number)
        {
            return true;
        }
        if (token.Type != LessonTokenType.Word)
        {
            return false;
        }
        var ch = token.Value.Span[0];
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
            if (lexer.Current.Type == LessonTokenType.Word)
            {
                return TryAllUnderscore(ref lexer);
            }
            {
                bool x = NumberAndOptionalLetter(ref lexer);
                Debug.Assert(x);
            }
            lexer.TryConsume(TokenType.Whitespace);
            if (lexer.TryConsume('/'))
            {
                lexer.TryConsume(TokenType.Whitespace);
                if (!OptionalNumberOrCode(ref lexer))
                {
                    return false;
                }
            }
            {
                if (lexer.IsEmpty)
                {
                    return true;
                }
                var t = lexer.Current;
                // Move this out of here?
                if (t.Is(',')
                    || t.Type == TokenType.Whitespace
                    || t.Type == TokenType.EndOfLine)
                {
                    return true;
                }
            }
            return false;

            bool TryAllUnderscore(ref LexerScope lexer)
            {
                var t = lexer.Current;
                bool AllUnderscore()
                {
                    foreach (var x in t.Value.Span)
                    {
                        if (x != '_')
                        {
                            return false;
                        }
                    }
                    return true;
                }
                if (AllUnderscore())
                {
                    lexer.Move();
                    return true;
                }

                return false;
            }

            bool OptionalNumberOrCode(ref LexerScope lexer)
            {
                bool consumedAnything = false;
                if (lexer.TryConsume(LessonTokenType.Number))
                {
                    consumedAnything = true;
                }
                if (lexer.TryConsume(LessonTokenType.Word))
                {
                    consumedAnything = true;
                }
                return consumedAnything;
            }

            bool NumberAndOptionalLetter(ref LexerScope lexer)
            {
                if (!lexer.TryConsume(LessonTokenType.Number))
                {
                    return false;
                }
                TryConsumeSingleLetter(ref lexer);
                return true;
            }

            bool TryConsumeSingleLetter(ref LexerScope lexer)
            {
                if (lexer.IsEmpty)
                {
                    return false;
                }
                var t = lexer.Current;
                if (t.Type == LessonTokenType.Word
                    && t.Value.Length == 1
                    && char.IsLetter(t.Value.Span[0]))
                {
                    lexer.Move();
                    return true;
                }
                return false;
            }
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

internal enum ParsingStep
{
    Start,
    TimeOverride,

    // Star is used for notes.
    OptionalStarBeforeLessonName,

    // May appear.
    OptionalFrontModifierBeforeLessonName,
    LessonName,

    // Lesson modifiers.
    OptionalParens,

    // Subgroup may be specified before the teacher-room pair.
    OptionalFrontModifierBeforeTeacher,
    // May be repeated with more teacher-room pairs.
    MaybeFrontModifierAgain,

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

    public Span<T>.Enumerator GetEnumerator() => Items.GetEnumerator();
}

internal struct AttributeRoot() : IBasic<AttributeRoot>
{
    public ListWithCapacity<Modifier> Modifiers = new();
    public ListWithCapacity<TeacherNameWrapper> Teachers = new();
    public ReadOnlyMemory<char> Room; // maybe make this a list too
    public ReadOnlyMemory<char> Lesson; // maybe make this a list too

    public void CopyInto(ref AttributeRoot other)
    {
        Modifiers.CopyInto(ref other.Modifiers);
        Teachers.CopyInto(ref other.Teachers);
        other.Room = Room;
        other.Lesson = Lesson;
    }

    public void Clear()
    {
        Modifiers.Clear();
        Teachers.Clear();
        Room = default;
        Lesson = default;
    }
}

internal struct Modifier() : IBasic<Modifier>
{
    public ModifierKey Key;
    public ModifierValue Value;

    public void CopyInto(ref Modifier other)
    {
        Key.CopyInto(ref other.Key);
        Value.CopyInto(ref other.Value);
    }

    public void Clear()
    {
        Key.Clear();
        Value.Clear();
    }
}

internal record struct ModifierKey() : IBasic<ModifierKey>
{
    public SubGroup SubGroup = SubGroup.All;
    public LessonType LessonType = LessonType.Unspecified;
    public static ModifierKey Default => new();

    public void CopyInto(ref ModifierKey other) => other = this;
    public void Clear() => this = new();
}

// NOTE: this is a tagged union! it's only supposed to have one of these.
internal enum ModifierValueTag
{
    None,
    LessonType,
    Parity,
    GroupName,
    SubGroup,
    StartTime,
}

internal struct ModifierValue() : IBasic<ModifierValue>
{
    public ModifierValueTag Tag { get; private set; } = ModifierValueTag.None;

    public bool IsNull => Tag == ModifierValueTag.None;

    public LessonType LessonType
    {
        get;
        set
        {
            SetTag(ModifierValueTag.LessonType);
            field = value;
        }
    } = LessonType.Unspecified;

    public Parity Parity
    {
        get;
        set
        {
            SetTag(ModifierValueTag.Parity);
            field = value;
        }
    } = Parity.EveryWeek;

    public ReadOnlyMemory<char> GroupName
    {
        get;
        set
        {
            SetTag(ModifierValueTag.GroupName);
            field = value;
        }
    } = default;

    public SubGroup SubGroup
    {
        get;
        set
        {
            SetTag(ModifierValueTag.SubGroup);
            field = value;
        }
    } = SubGroup.All;

    public TimeOnly StartTime
    {
        get;
        set
        {
            SetTag(ModifierValueTag.StartTime);
            field = value;
        }
    } = default;

    private void SetTag(ModifierValueTag tag)
    {
        Debug.Assert(Tag == ModifierValueTag.None);
        Tag = tag;
    }

    public void CopyInto(ref ModifierValue other) => other = this;
    public void Clear() => this = new();
}

internal struct ParsingState() : IBasic<ParsingState>
{
    public ParsingStep Step = ParsingStep.Start;
    public ListWithCapacity<AttributeRoot> AttributeRoots = new();

    public bool FrontModifierAppliedBeforeLessonName { get; set; }
    public bool MustMakeNewAttributeRoot { get; set; }

    public ref AttributeRoot GetCurrentAttributeRoot()
    {
        if (MustMakeNewAttributeRoot)
        {
            MustMakeNewAttributeRoot = false;
            AttributeRoots.Add();
        }
        return ref AttributeRoots.Items[^1];
    }

    public ref Modifier AddModifier()
    {
        return ref GetCurrentAttributeRoot().Modifiers.Add();
    }

    public void CopyInto(ref ParsingState copy)
    {
        copy.Step = Step;
        AttributeRoots.CopyInto(ref copy.AttributeRoots);
        copy.FrontModifierAppliedBeforeLessonName = FrontModifierAppliedBeforeLessonName;
        copy.MustMakeNewAttributeRoot = MustMakeNewAttributeRoot;
    }

    public void Clear() => Reset();

    public void Reset()
    {
        Step = ParsingStep.TimeOverride;
        AttributeRoots.Clear();
        FrontModifierAppliedBeforeLessonName = default;
        MustMakeNewAttributeRoot = true;
    }

    public bool IsTerminalState
    {
        get
        {
            return Step is ParsingStep.Output
                or ParsingStep.Start
                // In this format, the teacher name and the room are optional
                or ParsingStep.OptionalFrontModifierBeforeTeacher
                or ParsingStep.OptionalParens
                or ParsingStep.OptionalParensBeforeRoom
                or ParsingStep.OptionalTeacherNameOrRoomName
                or ParsingStep.OptionalRoomName
                or ParsingStep.MaybeFrontModifierAgain;
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
    internal static void ThrowLessonAlreadySpecified() => throw new WrongFormatException("Lesson already specified");

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

    [DoesNotReturn]
    internal static void ExpectedArabSubGroup() => throw new WrongFormatException("Expected subgroup number as a arab numeral");

    [DoesNotReturn]
    internal static void ExpectedSubGroupNumberToBeOneDigit() => throw new WrongFormatException("Expected a one-digit number for the group");
}

internal ref struct ParsingContext
{
    public required ref readonly ParseLessonsParams Params;
    public ref ParsingState State => ref StateStack.Last();
    public required ref LexerScope Lexer;
    public required ref ParsingStateStack StateStack;

    internal readonly LexerScope LexerCopy => Lexer;
}

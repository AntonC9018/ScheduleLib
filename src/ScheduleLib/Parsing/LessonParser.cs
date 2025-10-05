// TODO: Remove the use of lists.

using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.Common;
using InvalidOperationException = System.InvalidOperationException;

namespace ScheduleLib.Parsing.Lesson;

public struct ParseLessonsParams()
{
    public LessonTypeParser LessonTypeParser = LessonTypeParser.Instance;
    public ParityParser ParityParser = ParityParser.Instance;
    public RoomParser RoomParser = RoomParser.Instance;
    public required IEnumerable<string> Lines;
    public required StringBuilder StringBuilder;
}

public struct TeacherName
{
    public NameParts<ReadOnlyMemory<char>> FirstName;
    public NameParts<ReadOnlyMemory<char>> LastName;
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

internal static class LessonTokenType
{
    public const TokenType ModifierGroupStart = (TokenType) '(';
    public const TokenType ModifierGroupEnd = (TokenType) ')';
    public const TokenType Word = TokenType.Invalid + 1;
    public const TokenType ShortWord = Word + 1;
    public const TokenType Separator = (TokenType) ',';
    public const TokenType Star = (TokenType) '*';
}

internal sealed class LessonTokenReader : ITokenReader
{
    public static readonly LessonTokenReader Instance = new();

    public TokenType Read(ref Parser parser)
    {
        if (SkipWhitespace(ref parser).SkippedAny)
        {
            return TokenType.Whitespace;
        }

        if (SkipRegular(ref parser).SkippedAny)
        {
            bool isShort = parser.ConsumeExactChar(WordHelper.ShortenedWordCharacter);
            var type = isShort ? LessonTokenType.ShortWord : LessonTokenType.Word;
            return type;
        }

        char ch = parser.Current;
        parser.Move();

        switch (ch)
        {
            case (char) LessonTokenType.ModifierGroupStart
                or (char) LessonTokenType.ModifierGroupEnd
                or (char) LessonTokenType.Star:
            {
                return (TokenType) ch;
            }
            case ',' or '-' or ';' or ':':
            {
                return LessonTokenType.Separator;
            }
            default:
            {
                return TokenType.Invalid;
            }
        }
    }

    public static ParserHelper.SkipResult SkipWhitespace(ref Parser parser)
    {
        return parser.Skip(new SkipWhitespaceButNotUnderscore());
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

    public static ParserHelper.SkipResult SkipRegular(ref Parser parser)
    {
        return parser.Skip(new SkipRegularImpl());
    }

    private struct SkipRegularImpl : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            return IsRegular(ch);
        }
    }

    private static readonly SearchValues<char> _regularChars = SearchValues.Create(@"/\_+#@&");
    public static bool IsRegular(char ch)
    {
        if (char.IsLetterOrDigit(ch))
        {
            return true;
        }
        if (_regularChars.Contains(ch))
        {
            return true;
        }
        return false;
    }
}

internal static class LessonLexerHelper
{
    public static bool IsAnyWord(this in Token token)
    {
        return token.Type is LessonTokenType.Word or LessonTokenType.ShortWord;
    }

    // After going through the list, it will set the end position in the lexer.
    public static ListEnumerable List(this ref LexerScope lexer)
    {
        return new(ref lexer);
    }

    public static bool ConsumeExactWord(this ref LexerScope lexer, ReadOnlySpan<char> word)
    {
        if (lexer.IsEmpty)
        {
            return false;
        }
        var t = lexer.Current;
        if (t.Type != LessonTokenType.Word)
        {
            return false;
        }

        if (!t.Value.Span.SequenceEqual(word))
        {
            return false;
        }
        lexer.Move();
        return true;
    }
}

internal ref struct ListEnumerable
{
    private ref LexerScope _lexer;

    public ListEnumerable(ref LexerScope lexer)
    {
        _lexer = ref lexer;
    }

    public ListEnumerator GetEnumerator()
    {
        return new(ref _lexer);
    }
}

internal ref struct ListEnumerator
{
    private ref LexerScope _lexer;
    private LexerPosition _end;
    private bool _isLast;

    public ListEnumerator(ref LexerScope lexer)
    {
        _lexer = ref lexer;

        // Undo the first move
        _end = new(lexer.Position.Value - 1);
    }

    public bool MoveNext()
    {
        _lexer._position = new(_end.Value + 1);
        if (_isLast)
        {
            return false;
        }

        var lexer = _lexer;
        while (true)
        {
            if (lexer.IsEmpty)
            {
                WrongFormatException.UnclosedParens();
            }
            var t = lexer.Current;
            if (t.Type == TokenType.EndOfLine)
            {
                WrongFormatException.ThrowUnclosedParenInLessonName();
            }
            if (t.Is(')'))
            {
                _isLast = true;
                _end = lexer.Position;
                return true;
            }
            if (t.Is(','))
            {
                _end = lexer.Position;
                return true;
            }
            lexer.Move();
        }
    }

    public readonly LimitedLexerScope Current => new(_lexer, _end);
}

public static class LessonParsingHelper
{
    private static readonly TokenTypeLabels _labels =
        LexerHelper.CreateLabelDict(typeof(LessonTokenType));

    public static Lexer CreateLexer(IEnumerator<string> lines)
    {
        return new(
            lines,
            LessonTokenReader.Instance,
            _labels);
    }

    public static IEnumerable<ParsedLesson> ParseLessons(ParseLessonsParams p)
    {
        ParsingState state = new();
        using var lines = p.Lines.GetEnumerator();
        var lexer = CreateLexer(lines);

        while (lexer.CanPeek())
        {
            if (lexer.Peek().Type
                is TokenType.EndOfLine
                or TokenType.Whitespace)
            {
                lexer.Move();
                continue;
            }

            if (lexer.Peek().Type == TokenType.EndOfStream)
            {
                lexer.Move();
                break;
            }

            if (state.Step == ParsingStep.Output)
            {
                state.Reset();
            }

            var stepBefore = state.Step;

            var lexerScope = lexer.Scope();
            var context = new ParsingContext
            {
                Params = ref p,
                Lexer = ref lexerScope,
                State = ref state,
            };
            DoParsingIter(context);

            if (stepBefore == state.Step
                && lexerScope.Position == default)
            {
                throw new InvalidOperationException("Infinite loop in the parser");
            }
            lexerScope.Apply();

            if (state.Step != ParsingStep.Output)
            {
                continue;
            }

            foreach (var x in DoOutput())
            {
                yield return x;
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
                c.State.Step = ParsingStep.LessonName;
                break;
            }
            case ParsingStep.LessonName:
            {
                var endPosition = FindPositionOfLastModifierGroup(c.Lexer);
                var name = CleanName(
                    c.Lexer.Until(endPosition),
                    c.Params.StringBuilder);
                if (name.Length == 0)
                {
                    WrongFormatException.ThrowEmptyCourseName();
                }
                c.State.LessonsInParsing.Add(new()
                {
                    LessonName = name.AsMemory(),
                });
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
                    var modifierValue = ParseOutModifier(c, ref lexer);
                    bool somethingSet = modifiers.Set(modifierValue);
                    if (!somethingSet)
                    {
                        throw new WrongFormatException("Modifier group that did nothing");
                    }
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
                        // I don't even know how to get here.
                        Debug.Assert(!c.Lexer.Current.Is(','));

                        c.State.Step = ParsingStep.OptionalRoomName;
                        return true;
                    }
                    if (c.Lexer.TryConsume(','))
                    {
                        c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                        return true;
                    }
                    c.State.Step = ParsingStep.OptionalSubGroup;
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

                static MaybeGeneralModifiersValue ParseOutModifier(ParsingContext c, ref LimitedLexerScope lexer)
                {
                    if (lexer.IsEmpty)
                    {
                        WrongFormatException.InvalidToken();
                    }
                    var t = lexer.Current;
                    if (t.Type != LessonTokenType.Word)
                    {
                        WrongFormatException.InvalidToken();
                    }

                    lexer.Move();

                    if (c.Params.LessonTypeParser.Parse(t.Value.Span) is { } lessonType)
                    {
                        return new()
                        {
                            LessonType = lessonType,
                        };
                    }
                    if (c.Params.ParityParser.Parse(t.Value.Span) is { } parity1)
                    {
                        return new()
                        {
                            Parity = parity1,
                        };
                    }

                    return new()
                    {
                        GroupName = t.Value,
                    };
                }
            }
            case ParsingStep.OptionalSubGroup:
            case ParsingStep.MaybeSubGroupAgain:
            {
                if (c.State.Step == ParsingStep.OptionalSubGroup)
                {
                    // Lesson names might be delimited by a comma.
                    if (c.Lexer.TryConsume(','))
                    {
                        c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                        break;
                    }
                }
                else
                {
                    // Just ignore the comma if we're here again.
                    // This handles the case where there are multiple subgroups.
                    if (c.Lexer.TryConsume(','))
                    {
                        // Skip whitespace
                        break;
                    }
                }

                var t = c.Lexer.Current;
                var lexer = c.Lexer;
                if (t.Type != LessonTokenType.Word
                    || char.IsNumber(t.Value.Span[0])
                    || !VerifyColon(ref lexer))
                {
                    SetNoSubgroup(c);
                    break;
                }

                var subgroup = new SubGroup(t.Value.Span.ToString());
                c.State.LastModiferIndex = c.State.DefaultModifiers.FindOrAdd(subgroup);

                c.Lexer.MoveTo(lexer.Position);
                c.State.Step = ParsingStep.OptionalTeacherNameOrRoomName;
                break;

                static bool VerifyColon(ref LexerScope lexer)
                {
                    lexer.Move();
                    if (lexer.IsEmpty)
                    {
                        return false;
                    }
                    if (!lexer.TryConsume(':'))
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
                    // Maybe should check how it was added and give an error if it was
                    // added through "subgroup:" rather than "subgroup-modifier" syntax.
                    c.State.LastModiferIndex = c.State.DefaultModifiers.FindOrAdd(SubGroup.All);
                    c.State.Step = ParsingStep.OptionalTeacherNameOrRoomName;
                }
            }
            case ParsingStep.RequiredTeacherNameOrRoomName:
            case ParsingStep.OptionalTeacherNameOrRoomName:
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
                bool success = Teacher(c, ref lexer);
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
                            WrongFormatException.InvalidToken();
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

                static bool Teacher(ParsingContext c, ref LexerScope lexer)
                {
                    TeacherName result = new();
                    if (!ParseTeacherName(c, ref result, ref lexer))
                    {
                        return false;
                    }

                    ValidateNotShort(result.LastName);
                    ValidateIfOneIsShortAllAreShort(result.FirstName);

                    {
                        var copy = lexer;
                        copy.TryConsume(TokenType.Whitespace);
                        if (copy.TryConsume(':'))
                        {
                            return false;
                        }
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

                static void ValidateIfOneIsShortAllAreShort(NameParts<ReadOnlyMemory<char>> name)
                {
                    bool oneIsShort = name.Any(x => !x.IsEmpty && !new WordSpan(x.Span).LooksFull);
                    bool allAreShortOrEmpty = name.All(x => x.IsEmpty || !new WordSpan(x.Span).LooksFull);
                    if (oneIsShort && !allAreShortOrEmpty)
                    {
                        WrongFormatException.ThrowInvalidDoubleName();
                    }
                }

                static bool ParseTeacherName(
                    ParsingContext c,
                    ref TeacherName res,
                    ref LexerScope lexer)
                {
                    var name1 = Name(ref lexer);
                    if (name1 == default)
                    {
                        return false;
                    }

                    res.LastName = name1;
                    var copy = lexer;

                    if (WhitespaceHandling_IsDone(c, ref copy))
                    {
                        return true;
                    }

                    var name2 = Name(ref copy);
                    if (name2 == default)
                    {
                        return true;
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
            LimitedLexerScope lexer,
            StringBuilder sb)
        {
            try
            {
                var listBuilder = new ListStringBuilder(sb);
                while (!lexer.IsEmpty)
                {
                    var t = lexer.Current;
                    lexer.Move();

                    if (t.Type == TokenType.Invalid)
                    {
                        WrongFormatException.InvalidToken();
                        return "";
                    }
                    if (t.IsAnyWord())
                    {
                        listBuilder.Append(t.Value.Span);
                        continue;
                    }
                    // The only allowed separators
                    if (t.Is(',') || t.Is(')'))
                    {
                        sb.Append(t.Value.Span);
                        continue;
                    }
                    if (t.Is('('))
                    {
                        listBuilder.Append(t.Value.Span);
                        listBuilder = new(sb, " ");
                        continue;
                    }
                    if (t.Is('-'))
                    {
                        sb.Append(t.Value.Span);
                        listBuilder = new(sb, " ");
                        continue;
                    }
                    if (t.Type == TokenType.Whitespace)
                    {
                        continue;
                    }
                    WrongFormatException.InvalidToken();
                }
            }
            catch
            {
                sb.Clear();
                throw;
            }
            return sb.ToStringAndClear();
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

internal readonly struct DefaultModifiersList()
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

internal readonly struct SubLessonModifiersList()
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

internal struct SubLessonInParsing()
{
    public ReadOnlyMemory<char> LessonName = default;
    public SubLessonModifiersList Modifiers = new();
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

internal struct SpecificModifiersValue()
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

internal struct DefaultModifiersValue()
{
    public GeneralModifiersValue General = new();
    public SpecificModifiersValue Specific = new();
}

internal struct DefaultModifiers()
{
    public DefaultModifiersValue Value = new();
    public required SubGroup SubGroup { get; init; }

    [UnscopedRef] public ref GeneralModifiersValue General => ref Value.General;
    [UnscopedRef] public ref SpecificModifiersValue Specific => ref Value.Specific;
}

internal readonly record struct SubLessonModifiersKey()
{
    public static SubLessonModifiersKey Default => new();
    public SubGroup SubGroup { get; init; } = SubGroup.All;
    public LessonType LessonType { get; init; } = LessonType.Unspecified;
}

internal struct SubLessonModifiers()
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

internal enum ParsingStep
{
    Start,
    TimeOverride,

    // Star is used for notes.
    OptionalStarBeforeLessonName,
    LessonName,

    // Lesson modifiers.
    OptionalParens,

    // Subgroup may be specified before the teacher-room pair.
    OptionalSubGroup,
    // May be repeated with more teacher-room pairs.
    MaybeSubGroupAgain,

    // Rooms generally begin with a number.
    RequiredTeacherNameOrRoomName,
    OptionalTeacherNameOrRoomName,

    // Room modifiers.
    OptionalParensBeforeRoom,
    // Only room allowed after room modifiers.
    OptionalRoomName,

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
    internal static void InvalidToken() => throw new WrongFormatException("Invalid Token");

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
    internal static void ThrowInvalidDoubleName() => throw new WrongFormatException($"Double names must have the second short name after the '{NameConstants.DoubleNameSeparator}'");

    [DoesNotReturn]
    internal static void InvalidLastName() => throw new WrongFormatException($"Last name must not be short");
}

internal ref struct ParsingContext
{
    public required ref readonly ParseLessonsParams Params;
    public required ref ParsingState State;
    public required ref LexerScope Lexer;

    internal readonly LexerScope LexerCopy => Lexer;
}

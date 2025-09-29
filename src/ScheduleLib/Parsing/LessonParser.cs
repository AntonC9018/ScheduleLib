// TODO: Remove the use of lists.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using ScheduleLib.Generation;
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
    public NameParts<ReadOnlyMemory<char>> Name;
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

public enum LessonTokenType
{
    Whitespace = ' ',
    ModifierGroupStart = '(',
    ModifierGroupEnd = ')',
    EndOfLine = 0x100,
    EndOfStream,
    Word,
    ShortWord,
    Invalid,
    Separator = ',',
    Star = '*',
}

public struct TokenSpan
{
    public required int Row;
    public required ParserPosition ColStart;
    public required ParserPosition ColEnd;
}

public struct Token
{
    public required LessonTokenType Type;
    public required ReadOnlyMemory<char> Value;
    public required TokenSpan Span;

    public readonly bool IsType(char ch)
    {
        Debug.Assert((int) Type < (1 << sizeof(char)));
        if ((char) Type == ch)
        {
            return true;
        }
        return false;
    }

    public readonly bool Is(char ch)
    {
        if (Value.Length == 1)
        {
            return Value.Span[0] == ch;
        }
        return false;
    }
}

public readonly record struct LexerPosition(int Value)
{
}
public ref struct LexerScope
{
    internal LexerPosition _position;
    private ref LessonLexer _lexer;

    public readonly LexerPosition Position => _position;
    private int _positionIndex
    {
        readonly get => _position.Value;
        set => _position = new(value);
    }

    public LexerScope(ref LessonLexer lexer, LexerPosition position = default)
    {
        _lexer = ref lexer;
        _positionIndex = position.Value;
    }

    public void Move(int amount = 1)
    {
        Debug.Assert(_lexer.CanPeek(amount));
        _positionIndex += amount;
    }

    public bool IsEmpty
    {
        get
        {
            if (!_lexer.CanPeek(_positionIndex))
            {
                return false;
            }
            var current = _lexer.Peek(_positionIndex);
            if (current.Type == LessonTokenType.EndOfStream)
            {
                return false;
            }
            return true;
        }
    }

    public Token Current => _lexer.Peek(_positionIndex);

    public readonly Token Peek(int offset)
    {
        int i = _positionIndex - 1 + offset;
        return _lexer.Peek(i);
    }

    public readonly bool CanPeek(int offset)
    {
        int i = _positionIndex - 1 + offset;
        if (!_lexer.CanPeek(i))
        {
            return true;
        }
        return false;
    }

    public bool Consume(LessonTokenType type)
    {
        if (Current.Type == type)
        {
            Move();
            return true;
        }
        return false;
    }

    public bool Consume(char ch)
    {
        if (Current.Is(ch))
        {
            Move();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Actually consumes the tokens.
    /// </summary>
    public void Apply()
    {
        _lexer.Move(_positionIndex);
    }
}

public struct LessonLexer
{
    private readonly IEnumerator<string> _lines;
    private readonly List<Token> _queue;
    private Parser _parser;
    private bool _hasOutputEndOfLine = true;
    private int _rowIndex;

    public LessonLexer(IEnumerator<string> lines)
    {
        _lines = lines;
        _queue = new();
        _parser = new("");
        _rowIndex = 0;
    }

    private TokenSpan SpanUntil(ParserPosition end)
    {
        return new()
        {
            Row = _rowIndex,
            ColStart = _parser.Position,
            ColEnd = end,
        };
    }

    private void AddCurrentToken(ParserPosition end, LessonTokenType type = default)
    {
        var span = SpanUntil(end);
        var value = _parser.SourceUntilExclusive(end);
        if (type == default)
        {
            Debug.Assert(value.Length == 1);
            type = (LessonTokenType) value.Span[0];
        }
        _queue.Add(new()
        {
            Type = type,
            Value = value,
            Span = span,
        });
        _parser.MoveTo(end);
    }

    private bool TryReadNextLine()
    {
        if (HasEndOfStream)
        {
            return false;
        }
        if (!_lines.MoveNext())
        {
            AddEndOfStream();
            return false;
        }
        _parser = new(_lines.Current);
        _hasOutputEndOfLine = false;
        return true;
    }

    public bool ReadTokens(int count)
    {
        while (true)
        {
            if (_queue.Count < count)
            {
                return true;
            }
            if (HasEndOfStream)
            {
                return false;
            }

            if (_parser.IsEmpty)
            {
                TryAddEndOfLine();
                TryReadNextLine();
                continue;
            }

            var bparser = _parser.BufferedView();
            if (Helper.SkipWhitespace(ref bparser).SkippedAny)
            {
                AddCurrentToken(bparser.Position, LessonTokenType.Whitespace);
                continue;
            }

            if (Helper.SkipRegular(ref bparser).SkippedAny)
            {
                bool isShort = bparser.ConsumeExactChar(WordHelper.ShortenedWordCharacter);
                var type = isShort ? LessonTokenType.ShortWord : LessonTokenType.Word;
                AddCurrentToken(bparser.Position, type);
                continue;
            }

            char ch = bparser.Current;
            bparser.Move();

            switch (ch)
            {
                case (char) LessonTokenType.ModifierGroupStart
                    or (char) LessonTokenType.ModifierGroupEnd
                    or (char) LessonTokenType.Star:
                {
                    AddCurrentToken(bparser.Position);
                    continue;
                }
                case ',' or '-' or ';' or ':':
                {
                    AddCurrentToken(bparser.Position, LessonTokenType.Separator);
                    continue;
                }
                default:
                {
                    AddCurrentToken(bparser.Position, LessonTokenType.Invalid);
                    continue;
                }
            }
        }
    }

    private static class Helper
    {
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

        public static bool IsRegular(char ch)
        {
            if (char.IsNumber(ch))
            {
                return true;
            }
            if (char.IsAscii(ch))
            {
                return true;
            }
            if (ch == '_')
            {
                return true;
            }
            if (ch == '/')
            {
                return true;
            }
            if (ch == '\\')
            {
                return true;
            }
            return false;
        }
    }

    private bool HasEndOfStream
    {
        get
        {
            if (_queue.Count == 0)
            {
                return false;
            }
            return _queue[^1].Type == LessonTokenType.EndOfStream;
        }
    }

    private bool AddEndOfStream()
    {
        Debug.Assert(HasEndOfStream);
        _queue.Add(new Token
        {
            Span = new()
            {
                Row = _rowIndex,
                // Gives 0 when reading from default.
                ColStart = _parser.Position,
                ColEnd = _parser.Position,
            },
            Type = LessonTokenType.EndOfStream,
            Value = ReadOnlyMemory<char>.Empty,
        });
        return true;
    }

    private bool TryAddEndOfLine()
    {
        if (_hasOutputEndOfLine)
        {
            return false;
        }
        _queue.Add(new Token
        {
            Span = new()
            {
                Row = _rowIndex,
                ColStart = _parser.EndPosition,
                ColEnd = _parser.EndPosition,
            },
            Type = LessonTokenType.EndOfLine,
            Value = ReadOnlyMemory<char>.Empty,
        });
        _hasOutputEndOfLine = true;
        return true;
    }


    public bool CanPeek(int offset = 1)
    {
        Debug.Assert(offset >= 1);
        return ReadTokens(offset);
    }

    public Token Peek(int offset = 1)
    {
        Debug.Assert(offset >= 1);
        if (ReadTokens(offset))
        {
            return _queue[offset - 1];
        }
        throw new InvalidOperationException("Not enough tokens");
    }

    public void Move(int amount = 1)
    {
        if (!CanPeek(amount))
        {
            Debug.Fail("Not enough tokens to skip");
            _queue.Clear();
            return;
        }

        _queue.RemoveRange(0, amount);
    }

    public bool IsEmpty()
    {
        return !CanPeek(1);
    }
}

internal static class LessonLexerHelper
{
    public static LexerScope Scope(this ref LessonLexer lexer)
    {
        return new LexerScope(ref lexer);
    }

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
        return true;
    }
}

internal ref struct ListEnumerable
{
    private LexerScope _lexer;
    private ref LexerPosition _end;

    public ListEnumerable(ref LexerScope lexer)
    {
        _lexer = lexer;
        _end = ref lexer._position;
    }

    public ListEnumerator GetEnumerator()
    {
        return new(_lexer, ref _end);
    }
}

internal ref struct ListEnumerator
{
    private LexerScope _lexer;
    private LexerPosition _end;
    private bool _isLast;
    private ref LexerPosition _endOutput;

    public ListEnumerator(LexerScope lexer, ref LexerPosition end)
    {
        _lexer = lexer;

        // Undo the first move
        _end = new(lexer.Position.Value - 1);
        _endOutput = ref end;
    }

    public bool MoveNext()
    {
        _lexer._position = new(_end.Value + 1);
        if (_isLast)
        {
            _endOutput = _lexer._position;
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
            if (t.Type == LessonTokenType.EndOfLine)
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

internal ref struct LimitedLexerScope
{
    private LexerScope _lexer;
    private readonly LexerPosition _endPosition;

    public LimitedLexerScope(LexerScope lexer, LexerPosition endPosition)
    {
        _lexer = lexer;
        _endPosition = endPosition;
    }

    public readonly Token Current => _lexer.Current;
    public readonly bool IsEmpty => !CanPeek(1);

    public readonly bool CanPeek(int offset)
    {
        // Check doesn't exceed end
        int i = _lexer.Position.Value - 1 + offset;
        if (i > _endPosition.Value)
        {
            return false;
        }
        return _lexer.CanPeek(offset);
    }

    public readonly Token Peek(int offset = 1)
    {
        Debug.Assert(CanPeek(offset));
        return _lexer.Peek(offset);
    }

    public void Move(int amount = 1)
    {
        _lexer.Move(amount);
    }
}

public static class LessonParsingHelper
{
    public static IEnumerable<ParsedLesson> ParseLessons(ParseLessonsParams p)
    {
        ParsingState state = new();
        using var lines = p.Lines.GetEnumerator();
        LessonLexer lexer = new(lines);

        while (lexer.CanPeek())
        {
            if (lexer.Peek().Type
                is LessonTokenType.EndOfLine
                or LessonTokenType.Whitespace)
            {
                lexer.Move();
                continue;
            }

            if (state.Step == ParsingStep.Output)
            {
                state.Reset();
            }

            var stepBefore = state.Step;

            var context = new ParsingContext
            {
                Params = ref p,
                Lexer = lexer.Scope(),
                State = ref state,
            };
            DoParsingIter(ref context);

            if (stepBefore == state.Step
                && context.Lexer.Position != default)
            {
                throw new InvalidOperationException("Infinite loop in the parser");
            }
            context.Lexer.Apply();

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

    private static void DoParsingIter(ref ParsingContext c)
    {
        switch (c.State.Step)
        {
            case ParsingStep.TimeOverride:
            case ParsingStep.Start:
            {
                if (ParseTime(c.Lexer) is { } time)
                {
                    c.State.CommonLesson.StartTime = time;
                }

                c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                break;
            }
            case ParsingStep.OptionalStarBeforeLessonName:
            {
                if (c.Lexer.Consume('*'))
                {
                    c.State.CommonLesson.HasStar = true;
                }
                c.State.Step = ParsingStep.LessonName;
                break;
            }
            case ParsingStep.LessonName:
            {
                var endPosition = FindPositionOfLastModifierGroup(c.Lexer);
                var name = CleanCourseName(ref c.Lexer, c.Params.StringBuilder, endPosition);
                if (name.Length == 0)
                {
                    WrongFormatException.ThrowEmptyCourseName();
                }
                c.State.LessonsInParsing.Add(new()
                {
                    LessonName = name.AsMemory(),
                });
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

                foreach (var l in c.Lexer.List())
                {
                    var lexer = l;

                    if (lexer.IsEmpty)
                    {
                        WrongFormatException.EmptyListItem();
                    }

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
                    if (c.Lexer.Consume('('))
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
                    if (c.Lexer.Consume(','))
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

                    var subGroup = new SubGroup(t.Span.ToString());
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
                    if (c.Lexer.Consume(','))
                    {
                        c.State.Step = ParsingStep.OptionalStarBeforeLessonName;
                        break;
                    }
                }
                else
                {
                    // Just ignore the comma if we're here again.
                    // This handles the case where there are multiple subgroups.
                    if (c.Lexer.Consume(','))
                    {
                        // Skip whitespace
                        break;
                    }
                }

                var t = c.Lexer.Current;
                if (t.Type != LessonTokenType.Word
                    || char.IsNumber(t.Value.Span[0])
                    || !VerifyColon(c.Lexer))
                {
                    SetNoSubgroup(c);
                    break;
                }

                c.Lexer.Move(2);

                var subgroup = new SubGroup(t.Value.Span.ToString());
                c.State.LastModiferIndex = c.State.DefaultModifiers.FindOrAdd(subgroup);

                c.State.Step = ParsingStep.OptionalTeacherNameOrRoomName;
                break;

                static bool VerifyColon(LexerScope lexer)
                {
                    lexer.Move();
                    if (lexer.IsEmpty)
                    {
                        return false;
                    }
                    if (!lexer.Consume(':'))
                    {
                        return false;
                    }
                    if (lexer.Current.Type != LessonTokenType.Whitespace)
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
                var skipResult = bparser.Skip(new SkipTeacher());
                if (!skipResult.SkippedAny
                    || (!bparser.IsEmpty && !IsTeacherSeparator(bparser.Current)))
                {
                    Debug.Assert(!IsTeacherNameChar(bparser.Current));
                    if (c.State.Step == ParsingStep.RequiredTeacherNameOrRoomName)
                    {
                        throw new WrongFormatException("Required teacher name after comma");
                    }

                    // We've already tried for room name.
                    AdvanceStepAfterRoom(c);
                    break;
                }

                bool success = Teacher(c, ref bparser);
                Debug.Assert(success, "Don't think this is possible");
                break;

                static bool Teacher(ParsingContext c, ref Parser bparser)
                {
                    ref var teacher = ref c.State.LastModifiers.Specific.NewTeacher();

                    {
                        var lastName = LastName(c, ref bparser);
                        if (!lastName.Any(x => x.IsEmpty))
                        {
                            teacher.LastName = lastName;
                            NextStep(c, ref bparser);
                            return true;
                        }

                        static void NextStep(ParsingContext c, ref Parser bparser)
                        {
                            if (bparser.IsEmpty)
                            {
                                c.State.Step = ParsingStep.OptionalRoomName;
                                c.Parser.MoveTo(bparser.Position);
                                return;
                            }
                            if (bparser.Current == ' ')
                            {
                                c.State.Step = ParsingStep.OptionalParensBeforeRoom;
                                c.Parser.MoveTo(bparser.Position);
                                return;
                            }

                            Debug.Assert(bparser.Current == ',');
                            c.State.Step = ParsingStep.RequiredTeacherNameOrRoomName;
                            bparser.Move();
                        }
                    }

                    {
                        var firstName = FirstName(c, ref bparser);
                        if (firstName.Any(x => !x.IsEmpty))
                        {
                            NextStep(c, ref bparser);
                            teacher.Name = firstName;
                            return true;
                        }

                        static void NextStep(ParsingContext c, ref Parser bparser)
                        {
                            c.Parser.MoveTo(bparser.Position);
                            c.State.Step = ParsingStep.TeacherLastName;
                        }
                    }

                    return false;
                }

                static NameParts<ReadOnlyMemory<char>> LastName(ParsingContext c, ref Parser bparser)
                {
                    if (!bparser.IsEmpty
                        && bparser.Current is not (' ' or ','))
                    {
                        return default;
                    }

                    return Name(c, ref bparser);
                }

                static NameParts<ReadOnlyMemory<char>> FirstName(ParsingContext c, ref Parser bparser)
                {
                    Debug.Assert(!bparser.IsEmpty);

                    if (bparser.Current != '.')
                    {
                        return default;
                    }
                    bparser.Move();

                    return Name(c, ref bparser);
                }

            }
            case ParsingStep.TeacherLastName:
            {
                var bparser = c.Parser.BufferedView();
                var skipResult = bparser.SkipUntilAny([' ', ',', '(']);
                if (!skipResult.SkippedAny)
                {
                    // Only the first name?
                    // This only works because it's a forward parser (no backtracking)
                    throw new WrongFormatException();
                }

                var lastName = Name(c, ref bparser);
                c.Parser.MoveTo(bparser.Position);
                ref var teacher = ref c.State.LastModifiers.Value.Specific.LastTeacher;
                teacher.LastName = lastName;

                // Handles the case when there's a space before the comma.
                // It's a case of terrible formatting, but we have got precedents.
                // Could move this into a separate step.
                c.Parser.SkipWhitespace();

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

        static string CleanCourseName(
            ref LexerScope lexer,
            StringBuilder sb,
            LexerPosition endPosition)
        {
            try
            {

                var listBuilder = new ListStringBuilder(sb);
                while (lexer.Position != endPosition)
                {
                    var t = lexer.Current;
                    lexer.Move();

                    if (t.Type == LessonTokenType.Invalid)
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
                    if (t.Is(',') || t.Is('-'))
                    {
                        sb.Append(t.Value.Span);
                        // No space after commas
                        listBuilder = new(sb, " ");
                        continue;
                    }
                    if (t.Is('(') || t.Is(')'))
                    {
                        listBuilder.Append(t.Value.Span);
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
            bool isInsideParen = true;

            while (true)
            {
                if (lexer.IsEmpty)
                {
                    break;
                }
                var t = lexer.Current;

                if (t.Is('('))
                {
                    endPosition = lexer.Position;
                    if (isInsideParen)
                    {
                        WrongFormatException.NestedParenInLessonName();
                    }
                    isInsideParen = true;
                }
                if (t.Type == LessonTokenType.EndOfLine)
                {
                    break;
                }
                if (lexer.Current.Is(')'))
                {
                    isInsideParen = true;
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

        static NameParts<ReadOnlyMemory<char>> Name(ParsingContext c, ref Parser bparser)
        {
            var ret = default(NameParts<ReadOnlyMemory<char>>);

            ret[0] = c.Parser.SourceUntilExclusive(bparser);
            if (bparser.IsEmpty)
            {
                return ret;
            }

            {
                // G.-M. is a precedent for a doubled first name.
                var doubleBufferedParser = bparser.BufferedView();

                {
                    var skipResult = doubleBufferedParser.SkipWhitespace();
                    if (skipResult.EndOfInput)
                    {
                        return ret;
                    }
                }

                if (!doubleBufferedParser.ConsumeExactString(NameConstants.DoubleNameSeparator))
                {
                    return ret;
                }

                bparser.MoveTo(doubleBufferedParser.Position);
                c.Parser.MoveTo(bparser.Position);
            }

            {
                var skipResult = bparser.SkipWhitespace();
                if (skipResult.EndOfInput)
                {
                    WrongFormatException.ThrowInvalidDoubleName();
                    return default;
                }
            }

            {
                var skipResult = bparser.SkipUntilAny(['.']);
                if (skipResult.EndOfInput)
                {
                    WrongFormatException.ThrowInvalidDoubleName();
                    return default;
                }
                bparser.Move();
            }

            ret[1] = c.Parser.SourceUntilExclusive(bparser);
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

            var roomName = c.Parser.SourceUntilExclusive(lexer);
            c.Parser.MoveTo(lexer.Position);

            ref var roomNameMem = ref c.State.LastModifiers.Specific.RoomName;
            if (!roomNameMem.IsEmpty)
            {
                WrongFormatException.ThrowRoomAlreadySpecified();
            }
            roomNameMem = roomName;
            return true;
        }
    }

    static TimeOnly? ParseTime(LexerScope lexer)
    {
        if (lexer.IsEmpty)
        {
            return null;
        }
        if (TimePart(lexer) is not { } hours)
        {
            return null;
        }
        if (lexer.IsEmpty)
        {
            return null;
        }
        if (!lexer.Consume(':'))
        {
            return null;
        }
        if (TimePart(lexer) is not { } mins)
        {
            return null;
        }
        return new TimeOnly(
            hour: (int) hours,
            minute: (int) mins);

        static uint? TimePart(LexerScope lexer)
        {
            var t = lexer.Current;
            lexer.Move();

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
        if (ch is '-')
        {
            return true;
        }
        if (char.IsLetter(ch))
        {
            return true;
        }
        return false;
    }

    private struct SkipTeacher : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            // if (IsTeacherSeparator(ch))
            // {
            //     return false;
            // }
            if (IsTeacherNameChar(ch))
            {
                return true;
            }
            return false;
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
            SkipRoom(ref lexer);
            if (lexer.IsEmpty)
            {
                return true;
            }
            if (IsNotRoom(lexer.Current))
            {
                return false;
            }
            return true;
        }
    }


    private static ParserHelper.SkipResult SkipRoom(ref Parser parser)
    {
        return parser.Skip(new SkipRoomImpl());
    }

    private static bool IsNotRoom(char ch)
    {
        if (ch is ';' or ':')
        {
            return true;
        }
        return false;
    }
    private static bool IsRoomSeparator(char ch)
    {
        if (ch is ',')
        {
            return true;
        }
        if (char.IsWhiteSpace(ch))
        {
            return true;
        }
        return false;
    }

    private readonly struct SkipRoomImpl : IShouldSkip
    {
        public bool ShouldSkip(char ch)
        {
            if (IsRoomSeparator(ch))
            {
                return false;
            }
            if (IsNotRoom(ch))
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
        lexer.Consume(LessonTokenType.Whitespace);
        if (!lexer.Consume(','))
        {
            return MediacorParseProgress.AfterConfirmFail;
        }
        lexer.Consume(LessonTokenType.Whitespace);
        if (lexer.ConsumeExactWord("etajul"))
        {
            return MediacorParseProgress.AfterConfirmFail;
        }

        if (lexer.IsEmpty)
        {
            return MediacorParseProgress.AfterConfirmFail;
        }

        {
            var lexerCopy = lexer;
            // Maybe limit the max count to skip?
            var res = SkipRoom(ref lexerCopy);
            if (!res.SkippedAny)
            {
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

file enum LessonEndSequence
{
    None,
    OpeningParen,
    Comma,
    Numbers,
}

file static class LessonEnd
{
    public static SkipResult SkipUntilLessonEnd(this ref Parser parser)
    {
        var algorithm = new SkipLessonImpl();
        var result = parser.SkipWindow(
            ref algorithm,
            minWindowSize: 1,
            maxWindowSize: 2);
        var match = parser.IsEmpty
            ? LessonEndSequence.None
            : WhichSequence(parser.PeekSpanMaxSize(2));
        return new()
        {
            EndOfInput = result.EndOfInput,
            Match = match,
        };
    }

    public static LessonEndSequence WhichSequence(ReadOnlySpan<char> window)
    {
        bool Compare(ReadOnlySpan<char> a, ReadOnlySpan<char> w)
        {
            if (a.Length > w.Length)
            {
                return false;
            }
            if (w[.. a.Length].Equals(a, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }
            return false;
        }

        if (Compare("(", window))
        {
            return LessonEndSequence.OpeningParen;
        }
        if (Compare(", ", window))
        {
            return LessonEndSequence.Comma;
        }
        if (window.Length == 1)
        {
            return LessonEndSequence.None;
        }
        // 2D, 3D but not 423Room
        if (char.IsNumber(window[0]) && char.IsNumber(window[1]))
        {
            return LessonEndSequence.Numbers;
        }
        return LessonEndSequence.None;
    }

    private struct SkipLessonImpl : IShouldSkipSequence
    {
        public bool ShouldSkip(ReadOnlySpan<char> window)
        {
            if (WhichSequence(window) == LessonEndSequence.None)
            {
                return true;
            }
            return false;
        }
    }

    public readonly struct SkipResult
    {
        public required bool EndOfInput { get; init; }
        public required LessonEndSequence Match { get; init; }
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

    // Teachers often have "F.Last" as the name format.
    TeacherLastName,

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

}

internal ref struct ParsingContext
{
    public required ref readonly ParseLessonsParams Params;
    public required ref ParsingState State;
    public required LexerScope Lexer;
}

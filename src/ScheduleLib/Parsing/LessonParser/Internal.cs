using System.Buffers;
using ScheduleLib.Parsing.Common;

namespace ScheduleLib.Parsing.Lesson.Internal;

public static class LessonTokenType
{
    public const TokenType ModifierGroupStart = (TokenType) '(';
    public const TokenType ModifierGroupEnd = (TokenType) ')';
    public const TokenType Word = TokenType.Invalid + 1;
    public const TokenType ShortWord = Word + 1;
    public const TokenType Separator = (TokenType) ',';
    public const TokenType Star = (TokenType) '*';
}

public sealed class LessonTokenReader : ITokenReader
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


public static class LessonLexerHelper
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

public ref struct ListEnumerable
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

public ref struct ListEnumerator
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


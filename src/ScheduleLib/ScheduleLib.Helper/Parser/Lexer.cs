using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ScheduleLib.Helper.Parsing;

public struct TokenSpan
{
    public required int Row;
    public required ParserPosition ColStart;
    public required ParserPosition ColEnd;
}

public record struct Token
{
    public required TokenType Type;
    public required ReadOnlyMemory<char> Value;
    public required TokenSpan Span;

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

public interface ILexer
{
    public bool CanPeek(int offset = 1);
    public Token Peek(int offset = 1);
    public void Move(int amount = 1);
}

public static class ClassLexerExtensions
{
    extension<T>(T lexer) where T : class, ILexer
    {
        public LexerStructWrapper Wrap() => new(lexer);

        public bool IsEmpty
        {
            get
            {
                var t = lexer.Wrap();
                return t.IsEmpty;
            }
        }

        public Token Current
        {
            get
            {
                var t = lexer.Wrap();
                return t.Current;
            }
        }

        public bool TryConsumeAny(ReadOnlySpan<TokenType> types)
        {
            var t = lexer.Wrap();
            return t.TryConsumeAny(types);
        }

        public bool TryConsume(TokenType type)
        {
            var t = lexer.Wrap();
            return t.TryConsume(type);
        }

        public bool TryConsumeAny(ReadOnlySpan<char> chars)
        {
            var t = lexer.Wrap();
            return t.TryConsumeAny(chars);
        }

        public bool TryConsume(char ch)
        {
            var t = lexer.Wrap();
            return t.TryConsume(ch);
        }

        public ReadOnlyMemory<char> Concat(ConcatParams p)
        {
            var t = lexer.Wrap();
            return t.Concat(p);
        }

    }
}

public static class StructLexerExtensions
{
    extension<T>(ref T lexer) where T : struct, ILexer
    {
        public bool IsEmpty => !lexer.CanPeek();
        public Token Current => lexer.Peek();

        public bool TryConsumeAny(ReadOnlySpan<TokenType> types)
        {
            if (lexer.IsEmpty)
            {
                return false;
            }
            foreach (var type in types)
            {
                if (lexer.TryConsume(type))
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryConsume(TokenType type)
        {
            if (lexer.IsEmpty)
            {
                return false;
            }
            if (lexer.Current.Type == type)
            {
                lexer.Move();
                return true;
            }
            return false;
        }

        public bool TryConsumeAny(ReadOnlySpan<char> chars)
        {
            foreach (var ch in chars)
            {
                if (lexer.TryConsume(ch))
                {
                    return true;
                }
            }
            return false;
        }

        public bool TryConsume(char ch)
        {
            if (lexer.Current.Is(ch))
            {
                lexer.Move();
                return true;
            }
            return false;
        }

        public ReadOnlyMemory<char> Concat(ConcatParams p)
        {
            if (lexer.IsEmpty)
            {
                return ReadOnlyMemory<char>.Empty;
            }
            var first = lexer.Peek(1);
            if (first.Type != p.ConcattedType)
            {
                return ReadOnlyMemory<char>.Empty;
            }
            lexer.Move();
            if (!CanAppendOneMore(ref lexer))
            {
                return first.Value;
            }
            p.StringBuilder.Append(first.Value.Span);

            while (true)
            {
                p.StringBuilder.Append(p.WhitespaceReplacer);
                p.StringBuilder.Append(lexer.Peek(2).Value.Span);
                lexer.Move(2);

                if (!CanAppendOneMore(ref lexer))
                {
                    return p.StringBuilder.ToStringAndClear().AsMemory();
                }
            }

            bool CanAppendOneMore(ref T lexer)
            {
                if (!lexer.CanPeek(2))
                {
                    return false;
                }
                if (lexer.Peek(1).Type != TokenType.Whitespace)
                {
                    return false;
                }
                if (lexer.Peek(2).Type != p.ConcattedType)
                {
                    return false;
                }
                return true;
            }
        }

    }
}

public readonly record struct ConcatParams()
{
    public string WhitespaceReplacer { get; init; } = " ";
    public required StringBuilder StringBuilder { get; init; }
    public required TokenType ConcattedType { get; init; }
}

public readonly struct LexerStructWrapper : ILexer
{
    private readonly ILexer _lexer;

    public LexerStructWrapper(ILexer lexer)
    {
        _lexer = lexer;
    }

    public readonly bool CanPeek(int offset = 1) => _lexer.CanPeek(offset);
    public readonly Token Peek(int offset = 1) => _lexer.Peek(offset);
    public readonly void Move(int amount = 1) => _lexer.Move(amount);
}

public struct LexerScope : ILexer
{
    internal LexerPosition _position;
    internal readonly Lexer _lexer;

    public readonly LexerPosition Position => _position;
    internal int PositionIndex
    {
        readonly get => _position.Value;
        set => _position = new(value);
    }

    public LexerScope(Lexer lexer, LexerPosition position = default)
    {
        _lexer = lexer;
        PositionIndex = position.Value;
    }

    public void MoveTo(LexerPosition position)
    {
        int offset = position.Value;
        Debug.Assert(offset >= 0);
        Debug.Assert(_lexer.CanPeek(offset + 1));
        Debug.Assert(PositionIndex <= offset);
        PositionIndex = offset;
    }

    public void Move(int amount = 1)
    {
        Debug.Assert(CanPeek(amount));
        PositionIndex += amount;
    }

    public readonly Token Peek(int offset)
    {
        int i = PositionIndex + offset;
        return _lexer.Peek(i);
    }

    public readonly bool CanPeek(int offset)
    {
        int i = PositionIndex + offset;
        if (!_lexer.CanPeek(i))
        {
            return false;
        }

        var current = _lexer.Peek(i);
        if (current.Type == TokenType.EndOfStream)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Actually consumes the tokens.
    /// </summary>
    public void Apply()
    {
        if (PositionIndex > 0)
        {
            _lexer.Move(PositionIndex);
        }
    }

    public readonly override string ToString()
    {
        return _lexer.ToStringImpl(start: _position);
    }
}

public enum TokenType
{
    Whitespace = ' ',
    EndOfLine = 0x100,
    EndOfStream,
    Invalid,
}

public interface ITokenReader
{
    public TokenType Read(ref Parser parser);
}

public sealed class Lexer : ILexer
{
    public readonly TokenTypeLabels TokenTypeLabels;

    private IEnumerator<ReadOnlyMemory<char>>? _lines;
    private readonly ITokenReader _readImpl;
    // Just removing from start, since not much is queued usually
    // It's better to use a ring queue
    internal readonly List<Token> _queue;
    private Parser _parser;
    private bool _hasOutputEndOfLine;
    private bool _hasOutputEndOfStream;
    private int _rowIndex;

    public Lexer(
        ITokenReader readImpl,
        TokenTypeLabels tokenTypeLabels)
    {
        _lines = null;
        _queue = new();
        Reset(null!);
        _rowIndex = 0;
        TokenTypeLabels = tokenTypeLabels;
        _readImpl = readImpl;
    }

    public void Reset(IEnumerator<ReadOnlyMemory<char>> lines)
    {
        _lines = lines;
        _queue.Clear();
        _parser = new("");
        _rowIndex = 0;
        _hasOutputEndOfLine = true;
        _hasOutputEndOfStream = false;
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

    internal string ToStringImpl(
        LexerPosition start = default,
        LexerPosition end = default)
    {
        if (start == default)
        {
            start = new(0);
        }
        if (end == default)
        {
            end = new(_queue.Count);
        }

        var sb = new StringBuilder();
        sb.Append("[");
        var list = new ListStringBuilder(sb, ", ");
        var span = CollectionsMarshal.AsSpan(_queue);
        var spanSlice = span[start.Value .. end.Value];
        foreach (var t in spanSlice)
        {
            var type = TokenTypeLabels.Get(t.Type);
            list.Append($"{type} - {t.Value.Span}");
        }
        sb.Append("]");
        return sb.ToString();
    }

    public override string ToString()
    {
        return ToStringImpl();
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

    private void AddCurrentToken(ParserPosition end, TokenType type = default)
    {
        var span = SpanUntil(end);
        var value = _parser.SourceUntilExclusive(end);
        if (type == default)
        {
            Debug.Assert(value.Length == 1);
            type = (TokenType) value.Span[0];
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
        Debug.Assert(_lines is not null, "Initialize before use");

        if (HasEndOfStream)
        {
            return false;
        }
        if (!_lines.MoveNext())
        {
            AddEndOfStream();
            return false;
        }
        _rowIndex += 1;
        _parser = new(_lines.Current);
        _hasOutputEndOfLine = false;
        return true;
    }

    private bool ReadTokens(int count)
    {
        while (true)
        {
            if (_queue.Count >= count)
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
            var tokenType = _readImpl.Read(ref bparser);
            AddCurrentToken(bparser.Position, tokenType);
        }
    }

    private bool HasEndOfStream => _hasOutputEndOfStream;

    private bool AddEndOfStream()
    {
        Debug.Assert(!HasEndOfStream);
        _queue.Add(new Token
        {
            Span = new()
            {
                Row = _rowIndex,
                // Gives 0 when reading from default.
                ColStart = _parser.Position,
                ColEnd = _parser.Position,
            },
            Type = TokenType.EndOfStream,
            Value = ReadOnlyMemory<char>.Empty,
        });
        _hasOutputEndOfStream = true;
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
            Type = TokenType.EndOfLine,
            Value = ReadOnlyMemory<char>.Empty,
        });
        _hasOutputEndOfLine = true;
        return true;
    }
}

public static class LexerHelper
{
    public static LexerScope Scope(this Lexer lexer)
    {
        return new LexerScope(lexer);
    }

    public static LimitedLexerScope Until(this LexerScope lexer, LexerPosition end)
    {
        return new LimitedLexerScope(lexer, end);
    }

    public static bool ConsumeMultiple(this ref LexerScope lexer, ReadOnlySpan<TokenType> types)
    {
        bool consumed = false;
        while (true)
        {
            if (C(ref lexer, types))
            {
                consumed = true;
                continue;
            }
            break;
        }
        if (consumed)
        {
            return true;
        }
        return false;

        static bool C(ref LexerScope lexer, ReadOnlySpan<TokenType> types)
        {
            foreach (var t in types)
            {
                if (lexer.TryConsume(t))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public static TokenTypeLabels CreateLabels(Type type)
    {
        var builder = ImmutableDictionary.CreateBuilder<TokenType, string>();
        foreach (var baseValue in Enum.GetValues(typeof(TokenType)))
        {
            var v = (TokenType) baseValue;
            builder.Add(v, v.ToString());
        }

        if (type.IsEnum)
        {
            var values = Enum.GetValues(type);
            foreach (var v in values)
            {
                var name = Enum.GetName(type, v)!;
                builder.Add((TokenType) v, name);
            }
        }
        else
        {
            var fields = type.GetFields(BindingFlags.Static | BindingFlags.Public);
            foreach (var field in fields)
            {
                if (field.FieldType != typeof(TokenType))
                {
                    continue;
                }
                var value = (TokenType) field.GetValue(null)!;
                builder.Add(value, field.Name);
            }
        }

        return new(builder.ToImmutable());
    }
}

public readonly record struct TokenTypeLabels(
    ImmutableDictionary<TokenType, string> Dict)
{
    public TokenTypeLabels Create(Type t)
    {
        return LexerHelper.CreateLabels(t);
    }

    public readonly string Get(TokenType t)
    {
        return Dict.GetValueOrDefault(t) ?? t.ToString();
    }
}

public struct LimitedLexerScope : ILexer
{
    private LexerScope _lexer;
    private readonly LexerPosition _endPosition;

    public LimitedLexerScope(LexerScope lexer, LexerPosition endPosition)
    {
        _lexer = lexer;
        _endPosition = endPosition;
    }

    public readonly LexerPosition Position => _lexer.Position;

    public readonly bool CanPeek(int offset)
    {
        // Check doesn't exceed end
        int i = _lexer.Position.Value + offset - 1;
        if (i >= _endPosition.Value)
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

    public void Move(int amount = 1) => _lexer.Move(amount);

    public readonly override string ToString()
    {
        return _lexer._lexer.ToStringImpl(
            _lexer._position,
            _endPosition);
    }
}


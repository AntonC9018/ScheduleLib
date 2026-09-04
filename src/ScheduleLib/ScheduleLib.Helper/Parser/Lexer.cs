using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ScheduleLib.Helper.Parsing;

public struct TokenSpan
{
    public required int Row;
    public required SequencePosition ColStart;
    public required SequencePosition ColEnd;

    public readonly bool IsEmpty => ColStart == ColEnd;
}

public record struct Token
{
    public readonly ReadOnlyMemory<char> Value => WholeLineMem[Span.ColStart.Index .. Span.ColEnd.Index];

    public required ReadOnlyMemory<char> WholeLineMem;
    public required TokenType Type;
    public required TokenSpan Span;

    public readonly bool Is(char ch)
    {
        if (Value.Length == 1)
        {
            return Value.Span[0] == ch;
        }
        return false;
    }

    public override string ToString() => Value.ToString();
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
    extension(Lexer lexer)
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
            return t.ConcatWithSpaceReplacement(p);
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

        public ReadOnlyMemory<char> ConcatWithSpaceReplacement(ConcatParams p)
        {
            if (lexer.IsEmpty)
            {
                return ReadOnlyMemory<char>.Empty;
            }
            var first = lexer.Current;
            if (!p.ConcattedTypes.Contains(first.Type))
            {
                return ReadOnlyMemory<char>.Empty;
            }
            lexer.Move();
            if (!CanAppendOneMore(ref lexer, p.ConcattedTypes))
            {
                return first.Value;
            }
            p.StringBuilder.Append(first.Value.Span);

            while (true)
            {
                if (lexer.Current.Type == TokenType.Whitespace)
                {
                    p.StringBuilder.Append(p.WhitespaceReplacer);
                    lexer.Move();
                }
                p.StringBuilder.Append(lexer.Current.Value);
                lexer.Move();

                if (!CanAppendOneMore(ref lexer, p.ConcattedTypes))
                {
                    var ret = p.StringBuilder.ToStringAndClear().AsMemory();
                    return ret;
                }
            }

            bool CanAppendOneMore(ref T lexer, ReadOnlySpan<TokenType> types)
            {
                if (lexer.IsEmpty)
                {
                    return false;
                }
                if (types.Contains(lexer.Current.Type))
                {
                    return true;
                }
                if (lexer.Current.Type == TokenType.Whitespace)
                {
                    if (types.Contains(lexer.Peek(2).Type))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

    }

    extension (LimitedLexerScope lexer)
    {
        public ReadOnlyMemory<char> Concat(
            StringBuilder? maybeUsedStringBuilder = null,
            string defaultNewLine = "\n")
        {
            if (maybeUsedStringBuilder != null)
            {
                Debug.Assert(maybeUsedStringBuilder.Length == 0);
            }

            if (lexer.IsEmpty)
            {
                return ReadOnlyMemory<char>.Empty;
            }

            var startTok = lexer.Current;
            var prevTok = lexer.Current;

            lexer.Move();
            if (lexer.IsEmpty)
            {
                return startTok.Value;
            }

            ReadOnlyMemory<char> MemUntilNow()
            {
                Debug.Assert(startTok.WholeLineMem.Equals(prevTok.WholeLineMem));
                var startPos = startTok.Span.ColStart;
                var prevEndPos = prevTok.Span.ColEnd;
                var untilNowStr = startTok.WholeLineMem[startPos.Index .. prevEndPos.Index];
                return untilNowStr;
            }

            bool isUsingStringBuilder = false;

            while (true)
            {
                if (!lexer.CanPeek())
                {
                    if (isUsingStringBuilder)
                    {
                        return maybeUsedStringBuilder!.ToStringAndClear().AsMemory();
                    }
                    else
                    {
                        return MemUntilNow();
                    }
                }

                var currentTok = lexer.Current;

                bool MustInitializeStringBuilder()
                {
                    if (isUsingStringBuilder)
                    {
                        return false;
                    }
                    if (!currentTok.WholeLineMem.Equals(prevTok.WholeLineMem))
                    {
                        return true;
                    }
                    if (currentTok.Span.ColStart != prevTok.Span.ColEnd)
                    {
                        return true;
                    }
                    return false;
                }

                if (MustInitializeStringBuilder())
                {
                    isUsingStringBuilder = true;
                    maybeUsedStringBuilder ??= new();
                    maybeUsedStringBuilder.Append(MemUntilNow());
                    if (prevTok.Span.IsEmpty)
                    {
                        AppendEmptyTokenAtEndOfBufferToStringBuilder(prevTok);
                    }
                }

                if (isUsingStringBuilder)
                {
                    if (currentTok.Value.Length != 0)
                    {
                        maybeUsedStringBuilder!.Append(currentTok.Value);
                    }
                    else
                    {
                        AppendEmptyTokenAtEndOfBufferToStringBuilder(currentTok);
                    }
                }

                prevTok = currentTok;
                lexer.Move();

                void AppendEmptyTokenAtEndOfBufferToStringBuilder(Token tok)
                {
                    if (tok.Type == TokenType.EndOfLine)
                    {
                        maybeUsedStringBuilder!.Append(defaultNewLine);
                    }
                    else
                    {
                        // empty token
                    }
                }
            }
        }
    }
}

public readonly ref struct ConcatParams()
{
    public string WhitespaceReplacer { get; init; } = " ";
    public required StringBuilder StringBuilder { get; init; }
    public required ReadOnlySpan<TokenType> ConcattedTypes { get; init; }
}

public readonly struct LexerStructWrapper : ILexer
{
    private readonly Lexer _lexer;

    public LexerStructWrapper(Lexer lexer)
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
            PositionIndex = 0;
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
    public TokenType Read(ref SequenceReader reader);
    public TokenTypeLabels Labels { get; }
}

public sealed class Lexer : ILexer
{
    public TokenTypeLabels TokenTypeLabels => _readImpl.Labels;

    private IEnumerator<ReadOnlyMemory<char>>? _lines;
    private readonly ITokenReader _readImpl;
    // Just removing from start, since not much is queued usually
    // It's better to use a ring queue
    internal readonly List<Token> _queue;
    private SequenceReader _sequenceReader;
    private bool _hasOutputEndOfLine;
    private bool _hasOutputEndOfStream;
    private int _rowIndex;

    public Lexer(ITokenReader readImpl)
    {
        _lines = null;
        _queue = new();
        Reset(null!);
        _rowIndex = 0;
        _readImpl = readImpl;
    }

    public (int Row, SequencePosition Position) Position
    {
        get
        {
            return (_rowIndex, _sequenceReader.Position);
        }
    }

    public void Reset(IEnumerator<ReadOnlyMemory<char>> lines)
    {
        _lines = lines;
        _queue.Clear();
        _sequenceReader = new("");
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

    private TokenSpan SpanUntil(SequencePosition end)
    {
        return new()
        {
            Row = _rowIndex,
            ColStart = _sequenceReader.Position,
            ColEnd = end,
        };
    }

    private void AddCurrentToken(SequencePosition end, TokenType type = default)
    {
        var span = SpanUntil(end);
        var value = _sequenceReader.SourceUntilExclusive(end);
        if (type == default)
        {
            Debug.Assert(value.Length == 1);
            type = (TokenType) value.Span[0];
        }
        var token = new Token
        {
            WholeLineMem = _sequenceReader.Source,
            Type = type,
            Span = span,
        };
        Debug.Assert(value.Equals(token.Value));

        _queue.Add(token);
        _sequenceReader.MoveTo(end);
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
        _sequenceReader = new(_lines.Current);
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

            if (_sequenceReader.IsEmpty)
            {
                TryAddEndOfLine();
                TryReadNextLine();
                continue;
            }
            var bparser = _sequenceReader.BufferedView();
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
            WholeLineMem = _sequenceReader.Source,
            Span = new()
            {
                Row = _rowIndex,
                // Gives 0 when reading from default.
                ColStart = _sequenceReader.Position,
                ColEnd = _sequenceReader.Position,
            },
            Type = TokenType.EndOfStream,
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
            WholeLineMem = _sequenceReader.Source,
            Span = new()
            {
                Row = _rowIndex,
                ColStart = _sequenceReader.EndPosition,
                ColEnd = _sequenceReader.EndPosition,
            },
            Type = TokenType.EndOfLine,
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

    public static bool ConsumeAllConsecutive(this ref LexerScope lexer, ReadOnlySpan<TokenType> types)
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

    public static bool ConsumeAllConsecutiveThatAreNot(this ref LexerScope lexer, TokenType type)
    {
        bool consumed = false;
        while (true)
        {
            if (lexer.IsEmpty)
            {
                return consumed;
            }
            if (lexer.Current.Type == type)
            {
                return consumed;
            }
            consumed = true;
            lexer.Move();
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

    extension (LexerScope lexer)
    {
        public void Apply(ref SequenceReader reader)
        {
            if (lexer.IsEmpty)
            {
                reader.MoveTo(reader.EndPosition);
            }
            else
            {
                var t = lexer.Current;
                var source = t.Value;
                if (FindOffset(source.Span, reader.PeekSpanUntilEnd()) is not { } offset)
                {
                    throw new InvalidOperationException("Cannot apply displacement to this parser, because the lexer is on a different line now");
                }

                reader.Move(offset);
            }
        }
    }

    public static unsafe int? FindOffset<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b)
    {
        void* bStart = Unsafe.AsPointer(ref MemoryMarshal.GetReference(b));
        void* aStart = Unsafe.AsPointer(ref MemoryMarshal.GetReference(a));
        void* bEnd   = Unsafe.AsPointer(ref Unsafe.Add(ref MemoryMarshal.GetReference(b), b.Length));

        if (aStart < bStart || aStart > bEnd)
        {
            return null;
        }

        int offset = (int) ((byte*) aStart - (byte*) bStart) / Unsafe.SizeOf<T>();
        return offset;
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

    public readonly bool CanPeek(int offset = 1)
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


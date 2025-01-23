using System.Diagnostics;

namespace ScheduleLib.Parsing;

public struct Parser
{
    private readonly string _input;
    private int _index;

    public Parser(string input) => _input = input;

    public readonly string Source => _input;
    public readonly bool IsEmpty => _index >= _input.Length;
    public readonly char PeekAt(int offset) => _input[_index + offset];
    public readonly bool CanPeekAt(int offset) => _index + offset < _input.Length;
    public readonly bool CanPeekCount(int size) => CanPeekAt(size - 1);
    public readonly ReadOnlySpan<char> PeekSpan(int size) => _input.AsSpan(_index, size);
    public readonly ReadOnlySpan<char> PeekSpanUntilPosition(int positionExclusive) => _input.AsSpan()[_index .. positionExclusive];
    public readonly char Current => _input[_index];
    public void Move(int x = 1) => _index += x;
    public void MoveTo(int position)
    {
        Debug.Assert(_index <= position);
        _index = position;
    }
    public void MovePast(int position)
    {
        _index = Math.Min(_input.Length, position + 1);
    }

    public int Position => _index;

    // Conceptually doesn't consume when moving, it just moves the window.
    // Currently just return a copy, because we only have a string impl and
    // I don't want it to get more abstract at this point.
    public readonly Parser BufferedView() => this;
    public readonly override string ToString() => _input[_index ..];
}

public interface IShouldSkip
{
    public bool ShouldSkip(char ch);
}

public interface IShouldSkipSequence
{
    public bool ShouldSkip(ReadOnlySpan<char> window);
}

public static class ParserHelper
{
    public static bool IsUpper(char ch)
    {
        return ch >= 'A' && ch <= 'Z';
    }

    public static bool IsLower(char ch)
    {
        return ch >= 'a' && ch <= 'z';
    }

    public struct SkipResult
    {
        public bool EndOfInput;
        public bool Satisfied;
        public bool SkippedAny;

        public static SkipResult EndOfInputResult => new()
        {
            EndOfInput = true,
        };
    }

    public static SkipResult SkipWindow<T>(
        this ref Parser parser,
        T impl,
        int windowSize)

        where T : struct, IShouldSkipSequence, allows ref struct
    {
        var ret = new SkipResult();
        while (true)
        {
            if (!parser.CanPeekCount(windowSize))
            {
                ret.EndOfInput = true;
                break;
            }

            var window = parser.PeekSpan(windowSize);
            if (!impl.ShouldSkip(window))
            {
                ret.Satisfied = true;
                break;
            }

            ret.SkippedAny = true;
        }
        return ret;
    }

    private ref struct SkipWindowUntilStringImpl : IShouldSkipSequence
    {
        private readonly ReadOnlySpan<string> _strings;
        public SkipWindowUntilStringImpl(ReadOnlySpan<string> strings) => _strings = strings;

        public bool ShouldSkip(ReadOnlySpan<char> window)
        {
            foreach (var str in _strings)
            {
                if (window.Equals(str, StringComparison.CurrentCultureIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }
    }
    public static SkipResult SkipUntil(this ref Parser parser, ReadOnlySpan<string> strings)
    {
        Debug.Assert(!strings.IsEmpty);
        var len = strings[0].Length;
        Debug.Assert(strings[1 ..].All(x => x.Length == len));
        return parser.SkipWindow(new SkipWindowUntilStringImpl(strings), len);
    }

    private static bool All<T>(this ReadOnlySpan<T> s, Func<T, bool> action)
    {
        foreach (var item in s)
        {
            if (!action(item))
            {
                return false;
            }
        }
        return true;
    }

    public static SkipResult Skip<T>(this ref Parser parser, T impl)
        where T : struct, IShouldSkip, allows ref struct
    {
        var ret = new SkipResult();
        while (true)
        {
            if (parser.IsEmpty)
            {
                ret.EndOfInput = true;
                break;
            }

            if (!impl.ShouldSkip(parser.Current))
            {
                break;
            }

            ret.SkippedAny = true;
            parser.Move();
        }
        return ret;
    }

    private struct WhitespaceSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => char.IsWhiteSpace(ch);
    }
    public static SkipResult SkipWhitespace(this ref Parser parser)
    {
        return parser.Skip(new WhitespaceSkip());
    }

    private struct NotWhitespaceSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => !char.IsWhiteSpace(ch);
    }
    public static SkipResult SkipNotWhitespace(this ref Parser parser)
    {
        return parser.Skip(new NotWhitespaceSkip());
    }

    private ref struct SkipUntilImpl : IShouldSkip
    {
        private readonly ReadOnlySpan<char> _chars;
        public SkipUntilImpl(ReadOnlySpan<char> chars) => _chars = chars;
        public bool ShouldSkip(char ch) => !_chars.Contains(ch);
    }
    public static SkipResult SkipUntil(
        this ref Parser parser,
        ReadOnlySpan<char> chars)
    {
        return parser.Skip(new SkipUntilImpl(chars));
    }

    public static ConsumeIntResult ConsumePositiveInt(this ref Parser parser, int length)
    {
        if (!parser.CanPeekCount(length))
        {
            return ConsumeIntResult.Error(ConsumeIntStatus.InputTooShort);
        }

        var numChars = parser.PeekSpan(length);
        if (!uint.TryParse(numChars, out uint ret))
        {
            return ConsumeIntResult.Error(ConsumeIntStatus.NotAnInteger);
        }

        parser.Move(length);
        return ConsumeIntResult.Ok(ret);
    }

    private struct NumberSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => char.IsNumber(ch);
    }
    public static SkipResult SkipNumbers(this ref Parser parser)
    {
        return parser.Skip(new NumberSkip());
    }

    public static TimeOnly? ParseTime(ref Parser parser)
    {
        var bparser = parser.BufferedView();
        if (!bparser.SkipNumbers().SkippedAny)
        {
            return null;
        }

        uint hours;
        {
            var numberSpan = parser.PeekSpanUntilPosition(bparser.Position);
            if (!uint.TryParse(numberSpan, out hours))
            {
                return null;
            }

            parser.MoveTo(bparser.Position);
        }

        {
            if (parser.Current != ':')
            {
                return null;
            }
            parser.Move();
        }

        uint minutes;
        {
            var result = parser.ConsumePositiveInt(length: 2);
            if (result.Status != ConsumeIntStatus.Ok)
            {
                return null;
            }

            minutes = result.Value;
        }

        {
            var timeSpan = new TimeSpan(
                hours: (int) hours,
                minutes: (int) minutes,
                seconds: 0);
            var ret = TimeOnly.FromTimeSpan(timeSpan);
            return ret;
        }
    }
}

public enum ConsumeIntStatus
{
    Ok,
    InputTooShort,
    NotAnInteger,
}

public record struct ConsumeIntResult(
    ConsumeIntStatus Status,
    uint Value = 0)
{
    public static ConsumeIntResult Ok(uint value) => new(ConsumeIntStatus.Ok, value);
    public static ConsumeIntResult Error(ConsumeIntStatus error)
    {
        Debug.Assert(error != ConsumeIntStatus.Ok);
        return new(error);
    }
}

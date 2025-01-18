using System.Diagnostics;

namespace App;

public struct Parser
{
    private readonly string _input;
    private int _index;

    public Parser(string input)
    {
        _input = input;
    }

    public readonly string Source => _input;
    public readonly bool IsEmpty => _index >= _input.Length;
    public readonly char Peek(int i) => _input[_index + i];
    public readonly bool CanPeek(int i) => _index + i < _input.Length;
    public readonly ReadOnlySpan<char> PeekSpan(int size) => _input.AsSpan(_index, size);
    public readonly ReadOnlySpan<char> PeekSpanUntilPosition(int positionExclusive) => _input.AsSpan()[_index .. positionExclusive];
    public readonly char Current => _input[_index];
    public void Move(int x = 1) => _index += x;
    public void MoveTo(int position)
    {
        Debug.Assert(_index <= position);
        _index = position;
    }
    public void MovePast(int position) => MoveTo(position + 1);

    public int Position => _index;

    // Conceptually doesn't consume when moving, it just moves the window.
    // Currently just return a copy, because we only have a string impl and
    // I don't want it to get more abstract at this point.
    public readonly Parser BufferedView() => this;
}

public interface IShouldSkip
{
    public bool ShouldSkip(char ch);
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

    public static bool Skip<T>(this ref Parser parser, T impl)
        where T : struct, IShouldSkip, allows ref struct
    {
        static bool Iter(ref Parser p, T impl)
        {
            if (p.IsEmpty)
            {
                return true;
            }

            if (!impl.ShouldSkip(p.Current))
            {
                return true;
            }

            p.Move();
            return false;
        }

        if (Iter(ref parser, impl))
        {
            return false;
        }

        while (true)
        {
            if (Iter(ref parser, impl))
            {
                return true;
            }
        }
    }

    private struct WhitespaceSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => char.IsWhiteSpace(ch);
    }
    public static bool SkipWhitespace(this ref Parser parser)
    {
        return parser.Skip(new WhitespaceSkip());
    }

    private struct NotWhitespaceSkip : IShouldSkip
    {
        public bool ShouldSkip(char ch) => !char.IsWhiteSpace(ch);
    }
    public static bool SkipNotWhitespace(this ref Parser parser)
    {
        return parser.Skip(new NotWhitespaceSkip());
    }

    public static ConsumeIntResult ConsumePositiveInt(this ref Parser parser, int length)
    {
        if (!parser.CanPeek(length))
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
    public static bool SkipNumbers(this ref Parser parser)
    {
        return parser.Skip(new NumberSkip());
    }

    public static TimeOnly? ParseTime(ref Parser parser)
    {
        var bparser = parser.BufferedView();
        if (!parser.SkipNumbers())
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

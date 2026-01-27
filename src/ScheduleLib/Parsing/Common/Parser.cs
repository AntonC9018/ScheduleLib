using System.Diagnostics;
using ScheduleLib.Generation;

namespace ScheduleLib.Parsing.Common;

public record struct Parser
{
    private readonly ReadOnlyMemory<char> _input;
    private int _index;

    public Parser(string input) => _input = input.AsMemory();
    public Parser(ReadOnlyMemory<char> input) => _input = input;

    public readonly ReadOnlyMemory<char> Source => _input;
    public readonly ReadOnlySpan<char> WholeSpan => _input.Span;
    public readonly bool IsEmpty => _index >= _input.Length;
    public readonly char PeekAt(int offset) => WholeSpan[_index + offset];
    public readonly bool CanPeekAt(int offset) => _index + offset < _input.Length;
    public readonly bool CanPeekCount(int size) => CanPeekAt(size - 1);
    public readonly int GetPeekCount(int desiredSize)
    {
        int remaining = _input.Length - _index;
        return Math.Min(remaining, desiredSize);
    }
    private readonly int AvailableCount => _input.Length - _index;
    public readonly ReadOnlySpan<char> PeekSpan(int size) => WholeSpan[_index .. (_index + size)];
    public readonly ReadOnlySpan<char> PeekSpanMaxSize(int size)
    {
        int s = Math.Min(AvailableCount, size);
        return WholeSpan[_index .. s];
    }

    public readonly ReadOnlySpan<char> PeekSpanUntilPosition(ParserPosition positionExclusive)
    {
        int start = _index;
        int end = positionExclusive.Index;
        return WholeSpan[start .. end];
    }

    public readonly ReadOnlySpan<char> PeekSpanUntilEnd() => WholeSpan[_index ..];
    public readonly char Current => WholeSpan[_index];
    public void Move(int x = 1) => _index += x;
    public void MoveTo(ParserPosition position)
    {
        Debug.Assert(_index <= position.Index);
        _index = position.Index;
    }
    public void MovePast(ParserPosition position)
    {
        _index = Math.Min(_input.Length, position.Index + 1);
    }

    // Abstraction for the sake of type safety.
    // Specifically, to prevent `PeekSpanUntilPosition(other.Current)` from compiling.
    public readonly ParserPosition Position => new(_index);
    public readonly ParserPosition EndPosition => new(_input.Length);

    // Conceptually doesn't consume when moving, it just moves the window.
    // Currently just return a copy, because we only have a string impl and
    // I don't want it to get more abstract at this point.
    public readonly Parser BufferedView() => this;
    public readonly override string ToString() => WholeSpan[_index ..].ToString();
}

public readonly record struct ParserPosition(int Index);

public readonly record struct ParserSegment(
    Parser Start,
    ParserPosition EndExclusive) : ISpanFormattable
{
    public string ToString(string? format, IFormatProvider? formatProvider) => $"{this}";
    public override string ToString() => $"{this}";

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        // TODO: Improve this, it doesn't print anything if the segment is empty.
        var span = Start.PeekSpanUntilPosition(EndExclusive);
        if (destination.Length < span.Length)
        {
            charsWritten = 0;
            return false;
        }
        span.CopyTo(destination);
        charsWritten = span.Length;
        return true;
    }
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
    public static bool IsUpperAscii(char ch)
    {
        return ch >= 'A' && ch <= 'Z';
    }

    public static bool IsLowerAscii(char ch)
    {
        return ch >= 'a' && ch <= 'z';
    }

    public static ParserSegment Segment(this Parser parser, ParserPosition until)
    {
        return new(parser, until);
    }

    public readonly struct SkipSequenceResult
    {
        private readonly SkipResult _result;
        private readonly int _match;

        public SkipSequenceResult(
            SkipResult result,
            int match)
        {
            _match = match;
            _result = result;
        }

        public bool EndOfInput => _result.EndOfInput;
        public bool SkippedAny => _result.SkippedAny;
        public bool Satisfied => _result.Satisfied;
        public int Match => _match;
    }

    public struct SkipResult
    {
        public bool EndOfInput;
        public bool SkippedAny;
        public readonly bool Satisfied => SkippedAny && !EndOfInput;

        public static SkipResult EndOfInputResult => new()
        {
            EndOfInput = true,
        };
    }

    public static SkipResult SkipWindow<T>(
        this ref Parser parser,
        ref T impl,
        int minWindowSize,
        int maxWindowSize)

        where T : struct, IShouldSkipSequence, allows ref struct
    {
        var ret = new SkipResult();
        while (true)
        {
            int peekCount = parser.GetPeekCount(maxWindowSize);
            if (peekCount < minWindowSize)
            {
                ret.EndOfInput = true;
                return ret;
            }

            var window = parser.PeekSpan(peekCount);
            if (!impl.ShouldSkip(window))
            {
                break;
            }

            ret.SkippedAny = true;
            parser.Move();
        }
        return ret;
    }

    private ref struct SkipWindowUntilStringImpl : IShouldSkipSequence
    {
        private readonly ReadOnlySpan<string> _strings;
        public int Match { get; private set; }
        public SkipWindowUntilStringImpl(ReadOnlySpan<string> strings)
        {
            _strings = strings;
            Match = -1;
        }

        public bool ShouldSkip(ReadOnlySpan<char> window)
        {
            for (int index = 0; index < _strings.Length; index++)
            {
                string str = _strings[index];
                if (window.Length < str.Length)
                {
                    continue;
                }

                if (window[.. str.Length].Equals(str, StringComparison.CurrentCultureIgnoreCase))
                {
                    Match = index;
                    return false;
                }
            }

            return true;
        }
    }
    public static SkipSequenceResult SkipUntilSequence(this ref Parser parser, ReadOnlySpan<string> strings)
    {
        Debug.Assert(!strings.IsEmpty);
        Debug.Assert(All(strings, x => x.Length != 0));

        int min = MinSize(strings);
        int max = MaxSize(strings);
        var algorithm = new SkipWindowUntilStringImpl(strings);
        var result = parser.SkipWindow(
            ref algorithm,
            minWindowSize: min,
            maxWindowSize: max);
        var ret = new SkipSequenceResult(
            result,
            algorithm.Match);
        return ret;

        static int MaxSize(ReadOnlySpan<string> s)
        {
            int max = 0;
            foreach (var item in s)
            {
                max = Math.Max(max, item.Length);
            }
            return max;
        }
        static int MinSize(ReadOnlySpan<string> s)
        {
            int min = int.MaxValue;
            foreach (var item in s)
            {
                min = Math.Min(min, item.Length);
            }
            return min;
        }
    }

    public static bool All<T>(ReadOnlySpan<T> s, Func<T, bool> action)
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
    public static SkipResult SkipUntilAny(
        this ref Parser parser,
        ReadOnlySpan<char> chars)
    {
        return parser.Skip(new SkipUntilImpl(chars));
    }

    private ref struct SkipUntilNotImpl : IShouldSkip
    {
        private readonly ReadOnlySpan<char> _chars;
        public SkipUntilNotImpl(ReadOnlySpan<char> chars) => _chars = chars;
        public bool ShouldSkip(char ch) => _chars.Contains(ch);
    }
    public static SkipResult SkipUntilNotAny(
        this ref Parser parser,
        ReadOnlySpan<char> chars)
    {
        return parser.Skip(new SkipUntilNotImpl(chars));
    }

    private ref struct SkipLettersImpl : IShouldSkip
    {
        public bool ShouldSkip(char ch) => char.IsLetter(ch);
    }
    public static SkipResult SkipLetters(this ref Parser parser)
    {
        return parser.Skip(new SkipLettersImpl());
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

    public static uint? ConsumePositiveIntWithMaxLength(
        this ref Parser parser,
        int maxLength)
    {
        var bparser = parser.BufferedView();
        for (int i = 0; i < maxLength; i++)
        {
            if (bparser.IsEmpty)
            {
                break;
            }
            if (!char.IsNumber(bparser.Current))
            {
                break;
            }
            bparser.Move();
        }

        var span = parser.PeekSpanUntilPosition(bparser.Position);
        if (span.Length == 0)
        {
            return null;
        }
        if (!uint.TryParse(span, out uint ret))
        {
            return null;
        }

        parser.MoveTo(bparser.Position);
        return ret;
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

    public static ReadOnlyMemory<char> SourceUntilEnd(this Parser p)
    {
        var ret = p.Source[p.Position.Index ..];
        return ret;
    }

    public static ReadOnlyMemory<char> SourceUntilExclusive(this Parser a, ParserPosition end)
    {
        var start = a.Position;
        return a.Source[start.Index .. end.Index];
    }

    public static ReadOnlyMemory<char> SourceUntilExclusive(this Parser a, Parser b)
    {
        Debug.Assert(a.Source.Equals(b.Source));

        var end = b.Position;
        return a.SourceUntilExclusive(end);
    }

    public static ReadOnlyMemory<char> PeekSource(this Parser a, int count)
    {
        Debug.Assert(a.CanPeekCount(count));
        var end = a.Position.Index + count;
        var ret = a.Source[a.Position.Index .. end];
        return ret;
    }

    public static bool ConsumeExactChar(
        ref this Parser parser,
        char expectedChar)
    {
        if (parser.IsEmpty)
        {
            return false;
        }
        if (parser.Current == expectedChar)
        {
            parser.Move();
            return true;
        }
        return false;
    }

    public static bool ConsumeExactString(
        ref this Parser parser,
        ReadOnlySpan<char> expectedString)
    {
        return ConsumeExactString(ref parser, expectedString, StringComparison.Ordinal);
    }

    public static bool ConsumeExactString(
        ref this Parser parser,
        ReadOnlySpan<char> expectedString,
        StringComparison stringComparison)
    {
        if (!parser.CanPeekCount(expectedString.Length))
        {
            return false;
        }

        var peek = parser.PeekSpan(expectedString.Length);
        if (!peek.Equals(expectedString, stringComparison))
        {
            return false;
        }

        parser.Move(expectedString.Length);
        return true;
    }

    public static ReadRomanResult ReadRoman(this ref Parser parser)
    {
        var bparser = parser.BufferedView();
        {
            var result = bparser.SkipUntilNotAny("IVX");
            if (!result.SkippedAny)
            {
                return ReadRomanResult.CreateError(ReadRomanStatus.NotRomanNumeralStart);
            }
        }
        {
            var numberSpan = parser.PeekSpanUntilPosition(bparser.Position);
            var number = NumberHelper.FromRoman(numberSpan);
            if (number is not { } n)
            {
                return ReadRomanResult.CreateError(ReadRomanStatus.NotRoman);
            }
            parser.MoveTo(bparser.Position);
            return ReadRomanResult.CreateOk(n);
        }
    }
}

public struct ReadRomanResult
{
    public int Number { get; private init; }
    public ReadRomanStatus Status { get; private init; }

    public static ReadRomanResult CreateOk(int roman)
    {
        return new()
        {
            Number = roman,
            Status = ReadRomanStatus.Ok,
        };
    }

    public static ReadRomanResult CreateError(ReadRomanStatus err)
    {
        Debug.Assert(err != ReadRomanStatus.Ok);
        return new()
        {
            Status = err,
        };
    }
}

public enum ReadRomanStatus
{
    Ok,
    NotRomanNumeralStart,
    NotRoman,
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

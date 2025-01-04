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

    public int Position => _index;

    // Conceptually doesn't consume when moving, it just moves the window.
    // Currently just return a copy, because we only have a string impl and
    // I don't want it to get more abstract at this point.
    public readonly Parser BufferedView() => this;
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

    public static void SkipWhitespace(this ref Parser parser)
    {
        bool Iter(ref Parser p)
        {
            if (p.IsEmpty)
            {
                return true;
            }

            if (p.Current != ' ')
            {
                return true;
            }

            p.Move();
            return false;
        }

        if (Iter(ref parser))
        {
            return;
        }

        while (true)
        {
            if (Iter(ref parser))
            {
                return;
            }
        }
    }
}


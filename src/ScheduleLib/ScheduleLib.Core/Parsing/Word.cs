using ScheduleLib;

public readonly ref struct WordSpan(ReadOnlySpan<char> v)
{
    public readonly ReadOnlySpan<char> Value = v;
    public readonly bool LooksFull => Value[^1] != WordHelper.ShortenedWordCharacter;

    /// <summary>
    /// Excludes the potential period at the end.
    /// </summary>
    public readonly ShortenedWordSpan Shortened
    {
        get
        {
            if (LooksFull)
            {
                return new(Value);
            }
            return new ShortenedWordSpan(Value[.. ^1]);
        }
    }

    public bool IsNull => Value.IsEmpty;
}

public readonly record struct Word(string Value)
{
    public readonly WordSpan Span => new(Value);
    public readonly bool LooksFull => Span.LooksFull;
    public static Word Empty => new("");
    public override string ToString() => Value;
    public static implicit operator WordSpan(Word v) => v.Span;
}

/// <summary>
/// Does not contain the delimiter at end.
/// </summary>
public readonly record struct ShortenedWord(string Value)
{
    public readonly ShortenedWordSpan Span => new(Value);
    public static implicit operator ShortenedWordSpan(ShortenedWord v) => v.Span;
}

/// <summary>
/// Does not contain the delimiter at end.
/// </summary>
public readonly ref struct ShortenedWordSpan(ReadOnlySpan<char> v)
{
    public readonly ReadOnlySpan<char> Value = v;
}

public enum CompareShortenedWordsResult
{
    Equal_Exactly,
    Equal_FirstBetter,
    Equal_SecondBetter,
    NotEqual,
}

public static class WordHelper
{
    public const char ShortenedWordCharacter = '.';

    public static bool IsEqual(this CompareShortenedWordsResult r)
    {
        return r is CompareShortenedWordsResult.Equal_FirstBetter
            or CompareShortenedWordsResult.Equal_SecondBetter
            or CompareShortenedWordsResult.Equal_Exactly;
    }

    public static bool IsEqual(this WordSpan a, WordSpan b)
    {
        var ret = Compare(a.Shortened, b.Shortened);
        return ret.IsEqual();
    }

    public static bool IsEitherShortForOther(this WordSpan a, ShortenedWordSpan b)
    {
        var r = Compare(a.Shortened, b);
        return r.IsEqual();
    }

    public static CompareShortenedWordsResult Compare(this ShortenedWordSpan a, ShortenedWordSpan b)
    {
        int len = int.Min(a.Value.Length, b.Value.Length);
        var a1 = a.Value[.. len];
        var b1 = b.Value[.. len];
        if (!IgnoreDiacriticsAndCaseComparer.Instance.Equals(a1, b1))
        {
            return CompareShortenedWordsResult.NotEqual;
        }
        int lenCompared = a.Value.Length - b.Value.Length;
        if (lenCompared == 0)
        {
            return CompareShortenedWordsResult.Equal_Exactly;
        }

        bool a1longer = lenCompared > 0;
        if (a1longer)
        {
            return CompareShortenedWordsResult.Equal_FirstBetter;
        }
        return CompareShortenedWordsResult.Equal_SecondBetter;
    }
}

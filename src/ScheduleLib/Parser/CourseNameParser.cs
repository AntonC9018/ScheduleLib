using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;

namespace ScheduleLib.Parsing.CourseName;

public struct ParsedCourseName()
{
    public List<CourseNameSegment> Segments = new();
}

public readonly ref struct WordSpan(ReadOnlySpan<char> v)
{
    public readonly ReadOnlySpan<char> Value = v;
    public readonly bool LooksFull => Value[^1] != Word.ShortenedWordCharacter;
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
}
public readonly record struct Word(string Value)
{
    public readonly WordSpan Span => new(Value);
    public readonly bool LooksFull => Span.LooksFull;
    public const char ShortenedWordCharacter = '.';
    public static implicit operator WordSpan(Word v) => v.Span;
}

public struct CourseNameSegment()
{
    public required Word Word;
    public CourseNameSegmentFlags Flags = new();
    public ReadOnlySpan<char> GetInitials()
    {
        Debug.Assert(Flags.IsInitials);
        return Word.Value;
    }
}

public struct CourseNameSegmentFlags()
{
    public bool IsInitials = false;
}

/// <summary>
/// Does not contain the delimiter at end.
/// </summary>
public readonly record struct ShortenedWord(string Value)
{
    public readonly ShortenedWordSpan Span => new(Value);
    public static implicit operator ShortenedWordSpan(ShortenedWord v) => v.Span;
}

public readonly ref struct ShortenedWordSpan(ReadOnlySpan<char> v)
{
    public readonly ReadOnlySpan<char> Value = v;
}

public sealed class CourseParseContext
{
    public readonly int MinUsefulWordLength;
    public readonly ImmutableHashSet<string> IgnoredFullWords;
    public readonly ImmutableHashSet<string> ProgrammingLanguages;
    public readonly ImmutableArray<ShortenedWord> IgnoredShortenedWords;

    public CourseParseContext(in Params p)
    {
        for (int i = 0; i < p.IgnoredShortenedWords.Length; i++)
        {
            var w = p.IgnoredShortenedWords[i];
            if (w[^1] == '.')
            {
                throw new InvalidOperationException("Just provide the words without the dot.");
            }
        }

        MinUsefulWordLength = p.MinUsefulWordLength;
        IgnoredFullWords = ImmutableHashSet.Create(StringComparer.CurrentCultureIgnoreCase, p.IgnoredFullWords);
        ProgrammingLanguages = ImmutableHashSet.Create(StringComparer.CurrentCultureIgnoreCase, p.ProgrammingLanguages);

        {
            var b = ImmutableArray.CreateBuilder<ShortenedWord>(p.IgnoredShortenedWords.Length);
            foreach (var x in p.IgnoredShortenedWords)
            {
                b.Add(new(x));
            }
            IgnoredShortenedWords = b.MoveToImmutable();
        }
    }

    public static CourseParseContext Create(in Params p) => new(p);

    public ref struct Params()
    {
        public int MinUsefulWordLength = 2;
        public ReadOnlySpan<string> IgnoredFullWords = [];
        public ReadOnlySpan<string> ProgrammingLanguages = [];
        public ReadOnlySpan<string> IgnoredShortenedWords = [];
    }
}

public enum CompareWordsResult
{
    Equal_FirstBetter,
    Equal_SecondBetter,
    NotEqual,
}

public static class CourseNameHelper
{
    public static bool IsEqual(this CompareWordsResult r)
    {
        return r is CompareWordsResult.Equal_FirstBetter
            or CompareWordsResult.Equal_SecondBetter;
    }

    public static ParsedCourseName Parse(
        this CourseParseContext context,
        ReadOnlySpan<char> course)
    {
        var wordRanges = course.Split(' ');
        var count = course.Count(' ') + 1;
        var array = ArrayPool<string>.Shared.Rent(count);
        try
        {
            int i = 0;
            foreach (var range in wordRanges)
            {
                var span = course[range];
                if (span.Length == 0)
                {
                    continue;
                }
                // Need a string to be able to look up in the hash sets.
                // kinda yikes.
                var wordString = course[range].ToString();
                array[i] = wordString;
                i++;
            }

            var strings = array.AsSpan(0, i);

            bool isAnyProgrammingLanguage = IsAnyProgrammingLanguage(strings);

            var ret = new ParsedCourseName();
            foreach (var s in strings)
            {
                var word = new Word(s);
                var segment = new CourseNameSegment
                {
                    Word = word,
                };

                if (context.IgnoredFullWords.Contains(s))
                {
                    continue;
                }
                if (ShouldIgnoreShort())
                {
                    continue;
                }
                if (isAnyProgrammingLanguage
                    && IsEitherShortForOther(word, new("programare")))
                {
                    continue;
                }
                if (!PrepareReturn())
                {
                    continue;
                }
                {
                    ret.Segments.Add(segment);
                }

                bool PrepareReturn()
                {
                    bool isProgrammingLanguage = IsProgrammingLanguage(word);
                    if (isProgrammingLanguage)
                    {
                        return true;
                    }

                    bool isAllCapital = IsInitials(s);
                    if (isAllCapital)
                    {
                        segment.Flags.IsInitials = true;
                        return true;
                    }

                    if (s.Length < context.MinUsefulWordLength)
                    {
                        return false;
                    }

                    // Regular word.
                    return true;
                }

                bool ShouldIgnoreShort()
                {
                    foreach (var shortenedWithoutDot in context.IgnoredShortenedWords)
                    {
                        if (IsEitherShortForOther(word.Span, shortenedWithoutDot.Span))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            return ret;
        }
        catch (Exception)
        {
            ArrayPool<string>.Shared.Return(array);
            throw;
        }

        bool IsAnyProgrammingLanguage(ReadOnlySpan<string> strings)
        {
            foreach (var s in strings)
            {
                var word = new Word(s);
                if (IsProgrammingLanguage(word))
                {
                    return true;
                }
            }
            return false;
        }

        bool IsProgrammingLanguage(Word word)
        {
            if (!word.LooksFull)
            {
                return false;
            }
            return context.ProgrammingLanguages.Contains(word.Value);
        }

        bool IsInitials(ReadOnlySpan<char> s)
        {
            foreach (var c in s)
            {
                if (!IsFine())
                {
                    return false;
                }

                bool IsFine()
                {
                    if (char.IsUpper(c))
                    {
                        return true;
                    }
                    if (char.IsNumber(c))
                    {
                        return true;
                    }
                    return false;
                }
            }
            return true;
        }
    }

    public static bool AreEqual(this WordSpan a, WordSpan b)
    {
        var ret = Compare(a.Shortened, b.Shortened);
        return ret.IsEqual();
    }

    public static bool IsEitherShortForOther(this WordSpan a, ShortenedWordSpan b)
    {
        var r = Compare(a.Shortened, b);
        return r.IsEqual();
    }

    public static bool IsFullVersionOf(this WordSpan a, ShortenedWordSpan b)
    {
        Debug.Assert(a.LooksFull);

        if (a.Value.Length < b.Value.Length)
        {
            return false;
        }

        var a1 = a.Value[.. b.Value.Length];
        return a1.Equals(b.Value, StringComparison.CurrentCultureIgnoreCase);
    }

    public static CompareWordsResult Compare(this ShortenedWordSpan a, ShortenedWordSpan b)
    {
        int len = int.Min(a.Value.Length, b.Value.Length);
        var a1 = a.Value[.. len];
        var b1 = b.Value[.. len];
        if (!a1.Equals(b1, StringComparison.CurrentCultureIgnoreCase))
        {
            return CompareWordsResult.NotEqual;
        }

        bool a1longer = a.Value.Length > b.Value.Length;
        if (a1longer)
        {
            return CompareWordsResult.Equal_FirstBetter;
        }
        return CompareWordsResult.Equal_SecondBetter;
    }

    private struct CourseIter(ParsedCourseName c)
    {
        private int Index = 0;
        private int InitialsIndex = 0;
        private readonly ParsedCourseName _courseName = c;

        public bool IsDone => Index >= _courseName.Segments.Count;
        private CourseNameSegment CurrentSegment => _courseName.Segments[Index];
        public WordSpan CurrentWord
        {
            get
            {
                var s = CurrentSegment;
                if (s.Flags.IsInitials)
                {
                    var all = s.GetInitials();
                    var singleLetterSlice = all.Slice(InitialsIndex, 1);
                    return new(singleLetterSlice);
                }
                return s.Word;
            }
        }
        public void Move()
        {
            var s = CurrentSegment;
            if (s.Flags.IsInitials)
            {
                InitialsIndex++;
                if (InitialsIndex < s.GetInitials().Length)
                {
                    return;
                }
                InitialsIndex = 0;
            }
            Index++;
        }

    }

    public static bool IsEqual(this ParsedCourseName self, ParsedCourseName other)
    {
        var iself = new CourseIter(self);
        var iother = new CourseIter(other);

        while (true)
        {
            if (iself.IsDone && iother.IsDone)
            {
                return true;
            }
            if (iself.IsDone)
            {
                return false;
            }
            if (iother.IsDone)
            {
                return false;
            }

            var selfword = iself.CurrentWord;
            var otherword = iother.CurrentWord;
            if (!AreEqual(selfword, otherword))
            {
                return false;
            }

            iself.Move();
            iother.Move();
        }
    }
}



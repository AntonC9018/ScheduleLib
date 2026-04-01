using System.Collections.Immutable;
using System.Diagnostics;
using ScheduleLib.Helper;

namespace ScheduleLib.Parsing.CourseName;

public readonly struct ParsedCourseName() : IEquatable<ParsedCourseName>
{
    public readonly List<CourseNameSegment> Segments = new();

    public bool Equals(ParsedCourseName other)
    {
        var ret = this.IsEqual(other);
        return ret;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not ParsedCourseName other)
        {
            return false;
        }
        return Equals(other);
    }

    public override int GetHashCode()
    {
        return Segments.GetHashCode();
    }
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

    public static CourseNameSegment AsInitials(char ch)
    {
        Debug.Assert(char.IsUpper(ch));
        return new()
        {
            Word = new Word(ch.ToString()),
            Flags = new CourseNameSegmentFlags
            {
                IsInitials = true,
            },
        };
    }
}

public struct CourseNameSegmentFlags()
{
    public bool IsInitials = false;
    public bool CanBeIgnored = false;
}

public sealed class CourseNameParserConfig
{
    public readonly int MinUsefulWordLength;
    public readonly ImmutableHashSet<string> IgnoredFullWords;
    public readonly ImmutableHashSet<string> ProgrammingLanguages;
    public readonly ImmutableArray<ShortenedWord> IgnoredShortenedWords;
    public readonly ImmutableArray<string> IgnoredProgrammingRelatedWords;

    public CourseNameParserConfig(in Params p)
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
        IgnoredProgrammingRelatedWords = [.. p.IgnoredProgrammingRelatedWords];

        {
            var b = ImmutableArray.CreateBuilder<ShortenedWord>(p.IgnoredShortenedWords.Length);
            foreach (var x in p.IgnoredShortenedWords)
            {
                b.Add(new(x));
            }
            IgnoredShortenedWords = b.MoveToImmutable();
        }
    }

    public ref struct Params()
    {
        public int MinUsefulWordLength = 2;
        public ReadOnlySpan<string> IgnoredFullWords = [];
        public ReadOnlySpan<string> ProgrammingLanguages = [];
        public ReadOnlySpan<string> IgnoredShortenedWords = [];
        public ReadOnlySpan<string> IgnoredProgrammingRelatedWords = [];
        public ReadOnlySpan<(string From, string To)> FullyMappedNames = [];
    }
}

public struct CourseNameParseOptions()
{
    /// <summary>
    /// If this is false, punctuation is considered an error.
    /// </summary>
    public bool IgnorePunctuation = false;
}

public static class CourseNameParsing
{
    public static ParsedCourseName Parse(
        this CourseNameParserConfig config,
        ReadOnlyMemory<char> course,
        CourseNameParseOptions options = default)
    {
        var words = new WordEnumerable(course.Span, options);
        using var buffer = new RentedBuffer<string>(words.Count());

        int i = 0;
        foreach (var word in words)
        {
            // Need a string to be able to look up in the hash sets.
            // kinda yikes.
            var wordString = word.ToString();
            buffer.Array[i] = wordString;
            i++;
        }

        var strings = buffer.Span;

        bool isAnyProgrammingLanguage = IsAnyProgrammingLanguage(strings);

        var ret = new ParsedCourseName();
        foreach (var s in strings)
        {
            var word = new Word(s);
            var segment = new CourseNameSegment
            {
                Word = word,
            };

            if (config.IgnoredFullWords.Contains(s))
            {
                segment.Flags.CanBeIgnored = true;
            }
            if (ShouldIgnoreShort())
            {
                continue;
            }
            if (!PrepareReturn())
            {
                continue;
            }
            {
                ret.Segments.Add(segment);
                continue;
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

                if (CheckMayIgnoreAsProgrammingWord())
                {
                    segment.Flags.CanBeIgnored = true;
                    return true;
                }

                if (s.Length < config.MinUsefulWordLength)
                {
                    segment.Flags.CanBeIgnored = true;
                    return true;
                }

                // Regular word.
                return true;
            }

            bool CheckMayIgnoreAsProgrammingWord()
            {
                if (!isAnyProgrammingLanguage)
                {
                    return false;
                }
                foreach (var w in config.IgnoredProgrammingRelatedWords)
                {
                    if (word.Span.IsEitherShortForOther(new(w)))
                    {
                        return true;
                    }
                }
                return false;
            }

            bool ShouldIgnoreShort()
            {
                foreach (var shortenedWithoutDot in config.IgnoredShortenedWords)
                {
                    if (word.Span.IsEitherShortForOther(shortenedWithoutDot.Span))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        return ret;

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
            return config.ProgrammingLanguages.Contains(word.Value);
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

    private static bool IsEqualRecursion(
        CourseIter a,
        CourseIter b)
    {
        if (a.IsDone && b.IsDone)
        {
            return true;
        }

        static bool TryIngore(
            CourseIter a,
            CourseIter b)
        {
            if (!a.IsDone && a.CanIgnoreCurrent)
            {
                foreach (var w in a.GetPossibleWords())
                {
                    var copy = a;
                    copy.Move(w.Type);

                    if (IsEqualRecursion(copy, b))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        if (TryIngore(a, b))
        {
            return true;
        }
        if (TryIngore(b, a))
        {
            return true;
        }

        if (a.IsDone)
        {
            return false;
        }
        if (b.IsDone)
        {
            return false;
        }

        foreach (var wa in a.GetPossibleWords())
        {
            foreach (var wb in b.GetPossibleWords())
            {
                var equal = wa.Word.Shortened.Compare(wb.Word.Shortened);
                if (wa.Type == WordType.Plain
                    && wb.Type == WordType.Plain
                    && (a.MightBeInitials || b.MightBeInitials))
                {
                    if (equal != CompareShortenedWordsResult.Equal_Exactly)
                    {
                        continue;
                    }
                }
                else if (!equal.IsEqual())
                {
                    continue;
                }

                var acopy = a;
                var bcopy = b;
                acopy.Move(wa.Type);
                bcopy.Move(wb.Type);
                if (IsEqualRecursion(acopy, bcopy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsEqual(this ParsedCourseName self, ParsedCourseName other)
    {
        var iself = new CourseIter(self);
        var iother = new CourseIter(other);
        return IsEqualRecursion(iself, iother);
    }

    private struct CourseIter(ParsedCourseName c)
    {
        private int Index = 0;
        private int InitialsIndex = 0;
        private readonly ParsedCourseName _courseName = c;

        public bool IsDone => Index >= _courseName.Segments.Count;
        private CourseNameSegment CurrentSegment => _courseName.Segments[Index];
        public bool CanIgnoreCurrent => CurrentSegment.Flags.CanBeIgnored;
        public bool MightBeInitials => CurrentSegment.Flags.IsInitials;

        public WordSpan GetCurrentWord(WordType wordType)
        {
            switch (wordType)
            {
                case WordType.Plain:
                {
                    if (InitialsIndex != 0)
                    {
                        return default;
                    }
                    return CurrentSegment.Word;
                }
                case WordType.Letter:
                {
                    var s = CurrentSegment;
                    if (!s.Flags.IsInitials)
                    {
                        return default;
                    }
                    var all = s.GetInitials();
                    var singleLetterSlice = all.Slice(InitialsIndex, 1);
                    return new(singleLetterSlice);
                }
                default:
                {
                    throw Unreachable();
                }
            }
        }

        public void Move(WordType t)
        {
            var s = CurrentSegment;
            switch (t)
            {
                case WordType.Plain:
                {
                    Debug.Assert(InitialsIndex == 0);
                    Index++;
                    break;
                }
                case WordType.Letter:
                {
                    Debug.Assert(s.Flags.IsInitials);
                    InitialsIndex++;
                    if (InitialsIndex < s.GetInitials().Length)
                    {
                        return;
                    }
                    InitialsIndex = 0;
                    Index++;
                    break;
                }
                default:
                {
                    throw Unreachable();
                }
            }
        }

        public WordEnumerable GetPossibleWords() => new(this);
        public readonly struct WordEnumerable
        {
            private readonly CourseIter _i;
            public WordEnumerable(CourseIter i) => _i = i;
            public Enumerator GetEnumerator() => new(_i);
            public struct Enumerator
            {
                private EnumMembers<WordType>.Enumerator _e;
                private CourseIter _i;

                public Enumerator(CourseIter i)
                {
                    _e = new EnumMembers<WordType>().GetEnumerator();
                    _i = i;
                }

                public readonly ref struct V
                {
                    public readonly WordType Type;
                    public readonly WordSpan Word;

                    public V(WordType type, WordSpan word)
                    {
                        Type = type;
                        Word = word;
                    }
                }

                public V Current => new(_e.Current, _i.GetCurrentWord(_e.Current));

                public bool MoveNext()
                {
                    while (true)
                    {
                        if (!_e.MoveNext())
                        {
                            return false;
                        }
                        if (Current.Word.IsNull)
                        {
                            continue;
                        }
                        return true;
                    }
                }
            }
        }
    }
}

public sealed class InvalidSeparatorException : Exception
{
    public InvalidSeparatorException(int position)
        : base("Invalid separator at position " + position)
    {
    }
}

internal readonly ref struct WordEnumerable
{
    private readonly CourseNameParseOptions _opts;
    private readonly ReadOnlySpan<char> _str;

    public WordEnumerable(
        ReadOnlySpan<char> str,
        CourseNameParseOptions opts)
    {
        _opts = opts;
        _str = str;
    }

    public Enumerator GetEnumerator() => new(this);

    public int Count()
    {
        var e = GetEnumerator();
        int count = 0;
        while (e.MoveNext())
        {
            count++;
        }
        return count;
    }

    public ref struct Enumerator
    {
        private readonly WordEnumerable _e;
        private int _currentIndex;
        private int _startIndex;
        private int _count;

        public Enumerator(WordEnumerable e)
        {
            _e = e;
            _currentIndex = 0;
            _startIndex = 0;
            _count = 0;
        }

        private enum SepResult
        {
            Yes,
            No,
            NotAllowed,
        }

        private static bool IsPunctuation(char ch)
        {
            // Consider this part of the word.
            // Maybe add the option to not do this.
            if (ch == WordHelper.ShortenedWordCharacter)
            {
                return false;
            }
            if (ch is ',' or ';' or ':' or '!' or '?' or '&')
            {
                return true;
            }
            return false;
        }

        private readonly SepResult IsSep(char ch)
        {
            if (IsPunctuation(ch))
            {
                if (_e._opts.IgnorePunctuation)
                {
                    return SepResult.Yes;
                }
                return SepResult.NotAllowed;
            }
            if (char.IsWhiteSpace(ch))
            {
                return SepResult.Yes;
            }
            return SepResult.No;
        }

        private readonly bool IsSep_Throw(char ch)
        {
            var r = IsSep(ch);
            if (r == SepResult.NotAllowed)
            {
                throw new InvalidSeparatorException(_currentIndex);
            }
            return r == SepResult.Yes;
        }

        public bool MoveNext()
        {
            // Skip until the first non-separator at the start
            if (_currentIndex == 0)
            {
                while (true)
                {
                    if (_currentIndex >= _e._str.Length)
                    {
                        return false;
                    }

                    var ch = _e._str[_currentIndex];
                    if (!IsSep_Throw(ch))
                    {
                        break;
                    }

                    _currentIndex++;
                }
            }

            if (_currentIndex >= _e._str.Length)
            {
                return false;
            }

            _startIndex = _currentIndex;
            _count = 0;

            // Find first separator.
            while (true)
            {
                _currentIndex++;
                _count++;

                if (_currentIndex >= _e._str.Length)
                {
                    return true;
                }

                char ch = _e._str[_currentIndex];
                if (IsSep_Throw(ch))
                {
                    break;
                }
            }

            // Skip consecutive separators.
            while (true)
            {
                _currentIndex++;
                if (_currentIndex >= _e._str.Length)
                {
                    break;
                }

                char ch = _e._str[_currentIndex];
                if (!IsSep_Throw(ch))
                {
                    break;
                }
            }
            return true;
        }

        public readonly ReadOnlySpan<char> Current
        {
            get
            {
                return _e._str.Slice(_startIndex, length: _count);
            }
        }
    }
}

internal enum WordType
{
    Plain,
    Letter,
}


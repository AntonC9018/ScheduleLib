using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Parsing.CourseName;

// TODO: This should probably be separated in 2.
public sealed class CourseNameUnifierModule
{
    internal readonly List<SlowCourse> SlowCourses = new();
    private readonly CourseNameParserConfig _parserConfig;

    public CourseNameUnifierModule(CourseNameParserConfig parserConfig)
    {
        _parserConfig = parserConfig;
    }

    public ref struct FindParams()
    {
        public required LookupModule Lookup;
        public required string CourseName;
        public CourseNameParseOptions ParseOptions = new();

        internal readonly CourseNameForParsing CourseNameForParsing => new()
        {
            CourseName = CourseName,
            ParseOptions = ParseOptions,
        };
    }

    public CourseId? Find(FindParams p)
    {
        if (p.Lookup.Courses.TryGetValue(p.CourseName, out var courseId))
        {
            return new(courseId);
        }

        var parsedCourseName = ParseCourseName(p.CourseNameForParsing);
        if (FindSlow(parsedCourseName) is { } slowCourseId)
        {
            p.Lookup.Courses.Add(p.CourseName, slowCourseId.Id);
            return slowCourseId;
        }

        return null;
    }

    public struct FindOrAddParams()
    {
        public required ScheduleBuilder Schedule;
        public required string CourseName;
        public CourseNameParseOptions ParseOptions = new();

        internal readonly CourseNameForParsing CourseNameForParsing => new()
        {
            CourseName = CourseName,
            ParseOptions = ParseOptions,
        };
    }

    public struct CourseNameForParsing
    {
        public required CourseNameParseOptions ParseOptions;
        public required string CourseName;
    }

    private ParsedCourseName ParseCourseName(CourseNameForParsing p)
    {
        var parsedCourse = _parserConfig.Parse(p.CourseName, p.ParseOptions);
        return parsedCourse;
    }

    private CourseId? FindSlow(ParsedCourseName parsedCourseName)
    {
        // TODO: N^2, use some sort of hash to make this faster.
        foreach (var t in SlowCourses)
        {
            if (t.Name.IsEqual(parsedCourseName))
            {
                continue;
            }
            return t.CourseId;
        }
        return null;
    }

    public CourseId FindOrAdd(in FindOrAddParams p)
    {
        ref var courseId = ref CollectionsMarshal.GetValueRefOrAddDefault(
            p.Schedule.LookupModule!.Courses,
            p.CourseName,
            out bool exists);

        if (exists)
        {
            return new(courseId);
        }

        var parsedCourse = ParseCourseName(p.CourseNameForParsing);
        if (FindSlow(parsedCourse) is { } slowCourseId)
        {
            courseId = slowCourseId.Id;
            return slowCourseId;
        }

        var result = p.Schedule.Courses.New();
        courseId = result.Id;
        ScheduleBuilderHelper.UpdateLookupAfterCourseAdded(p.Schedule);

        SlowCourses.Add(new(parsedCourse, new(result.Id)));

        return new(result.Id);
    }
}


public struct ParsedCourseName()
{
    public List<CourseNameSegment> Segments = new();
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
        ReadOnlySpan<char> course,
        CourseNameParseOptions options = default)
    {
        var words = new WordEnumerable(course, options);
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
                continue;
            }
            if (ShouldIgnoreShort())
            {
                continue;
            }
            if (ShouldIgnoreProgrammingWords())
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

                if (s.Length < config.MinUsefulWordLength)
                {
                    return false;
                }

                // Regular word.
                return true;
            }

            bool ShouldIgnoreProgrammingWords()
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
            if (!selfword.IsEqual(otherword))
            {
                return false;
            }

            iself.Move();
            iother.Move();
        }
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
            if (ch is ',' or ';' or ':' or '!' or '?')
            {
                return false;
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


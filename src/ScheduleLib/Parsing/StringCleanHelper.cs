namespace ScheduleLib.Parsing;

public static class StringCleanHelper
{
    public static WithoutConsecutiveSpacesEnumerable WithoutConsecutiveSpaces(this ReadOnlySpan<char> s)
    {
        return new(s);
    }
    public static WordsSeparatedWithSpacesEnumerable WordsSeparatedWithSpaces(this ReadOnlySpan<char> s)
    {
        return new(s);
    }
}

public readonly ref struct WordsSeparatedWithSpacesEnumerable
{
    private readonly ReadOnlySpan<char> _str;
    public WordsSeparatedWithSpacesEnumerable(ReadOnlySpan<char> str) => _str = str;
    public Enumerator GetEnumerator() => new(_str);

    public ref struct Enumerator
    {
        private WithoutConsecutiveSpacesEnumerable.Enumerator _withoutSpaces;
        private bool _shouldOutputSpace;

        public Enumerator(ReadOnlySpan<char> str)
        {
            _withoutSpaces = new(str);
            _shouldOutputSpace = false;
        }

        public readonly char Current
        {
            get
            {
                if (_shouldOutputSpace)
                {
                    return ' ';
                }
                return _withoutSpaces.Current;
            }
        }

        public bool MoveNext()
        {
            if (_shouldOutputSpace)
            {
                _shouldOutputSpace = false;
                return true;
            }

            bool isPrevPunctuation = _withoutSpaces.IsInitialized
                && char.IsPunctuation(_withoutSpaces.Current);
            if (!_withoutSpaces.MoveNext())
            {
                return false;
            }

            bool isCurrentSpace = _withoutSpaces.Current == ' ';
            if (!isCurrentSpace && isPrevPunctuation)
            {
                _shouldOutputSpace = true;
            }

            return true;
        }
    }
}

public readonly ref struct WithoutConsecutiveSpacesEnumerable
{
    private readonly ReadOnlySpan<char> _str;
    public WithoutConsecutiveSpacesEnumerable(ReadOnlySpan<char> str) => _str = str;
    public Enumerator GetEnumerator() => new(_str);

    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<char> _str;
        private bool _isSpace;
        private int _index;

        public Enumerator(ReadOnlySpan<char> str)
        {
            _str = str;
            _isSpace = false;
            _index = -1;
        }

        public readonly bool IsInitialized => _index != -1;

        public readonly bool IsDone
        {
            get
            {
                return _index >= _str.Length;
            }
        }

        public readonly char Current => _str[_index];

        public bool MoveNext()
        {
            while (true)
            {
                _index++;
                if (IsDone)
                {
                    return false;
                }

                bool isCurrentSpace = Current is ' ' or '\t' or '\r' or '\n';
                if (_isSpace && isCurrentSpace)
                {
                    continue;
                }

                _isSpace = isCurrentSpace;
                return true;
            }
        }
    }
}

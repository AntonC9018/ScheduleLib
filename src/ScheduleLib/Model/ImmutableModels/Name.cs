using System.Runtime.CompilerServices;
using System.Text;
using ScheduleLib.Parsing;

namespace ScheduleLib;

public record struct OptionalFirstNamePart
{
    public required string? Full;
    public required string? Short;

    public readonly string? Longer
    {
        get
        {
            if (Full is { } full)
            {
                return full;
            }
            if (Short is { } shortName)
            {
                return shortName;
            }
            return null;
        }
    }
    public readonly bool IsNull => Full is null && Short is null;
}

public record struct NameParts<T>()
{
    public required T A;
    public required T B;
}

public enum FirstNamePartIndex
{
    A,
    B,
    Count,
}

// This amount of boilerplate is seriously concerning.
// This should just work automatically, time to write a source gen.
public static class NameHelper
{
    public ref struct RefEnumerable<T>
    {
        internal readonly ref NameParts<T> _parts;

        public RefEnumerable(ref NameParts<T> parts)
        {
            _parts = ref parts;
        }
    }

    public static RefEnumerable<T> AsRef<T>(this ref NameParts<T> parts)
    {
        return new(ref parts);
    }

    public static RefEnumerator<T> GetEnumerator<T>(this RefEnumerable<T> parts)
    {
        return new(ref parts._parts);
    }

    public static Enumerator<T> GetEnumerator<T>(this NameParts<T> parts)
    {
        return new(parts);
    }

    public struct EnumeratorState()
    {
        private int _value = -1;

        public ref T GetRef<T>(ref NameParts<T> parts)
        {
            return ref NameHelper.GetRef(parts, (FirstNamePartIndex) _value);
        }

        public bool MoveNext()
        {
            _value++;
            return _value < 2;
        }
    }

    public ref struct RefEnumerator<T>
    {
        private readonly ref NameParts<T> _parts;
        private EnumeratorState _enumeratorState;

        public RefEnumerator(ref NameParts<T> parts)
        {
            _parts = ref parts;
            _enumeratorState = new();
        }

        public ref T Current => ref _enumeratorState.GetRef(ref _parts);
        public bool MoveNext() => _enumeratorState.MoveNext();
    }

    public struct Enumerator<T>
    {
        private readonly NameParts<T> _parts;
        private EnumeratorState _enumeratorState;

        public Enumerator(NameParts<T> parts)
        {
            _parts = parts;
            _enumeratorState = new();
        }

        public T Current => _enumeratorState.GetRef(ref Unsafe.AsRef(in _parts));
        public bool MoveNext() => _enumeratorState.MoveNext();
    }

    public static NameParts<U> Map<T, U>(this NameParts<T> n, Func<T, U> map)
    {
        var ret = default(NameParts<U>);
        var i = new EnumeratorState();
        while (i.MoveNext())
        {
            var a = i.GetRef(ref n);
            ref var b = ref i.GetRef(ref ret);
            b = map(a);
        }
        return ret;
    }

    public static void Update<T, U>(
        this ref NameParts<T> a,
        NameParts<U> input,
        Func<T, U, T> update)
    {
        var i = new EnumeratorState();
        while (i.MoveNext())
        {
            ref var fa = ref i.GetRef(ref a);
            var fb = i.GetRef(ref input);
            fa = update(fa, fb);
        }
    }

    public static bool All<T>(this NameParts<T> a, Func<T, bool> pred)
    {
        foreach (var x in a)
        {
            if (!pred(x))
            {
                return false;
            }
        }
        return true;
    }

    public static bool Any<T>(this NameParts<T> a, Func<T, bool> pred)
    {
        foreach (var x in a)
        {
            if (pred(x))
            {
                return true;
            }
        }
        return false;
    }

    public static bool EachEquals<T, U>(this NameParts<T> a, NameParts<U> b, Func<T, U, bool> pred)
    {
        var i = new EnumeratorState();
        while (i.MoveNext())
        {
            var fa = i.GetRef(ref a);
            var fb = i.GetRef(ref b);
            if (!pred(fa, fb))
            {
                return false;
            }
        }
        return true;
    }

    public static int Count<T>(this NameParts<T> a, Func<T, bool> pred)
    {
        int c = 0;
        foreach (var t in a)
        {
            if (pred(t))
            {
                c++;
            }
        }
        return c;
    }

    public static int CompareEach<T>(
        NameParts<T> a,
        NameParts<T> b,
        IComparer<T> comparer)
    {
        var i = new EnumeratorState();
        while (i.MoveNext())
        {
            var fa = i.GetRef(ref a);
            var fb = i.GetRef(ref b);
            var cmp = comparer.Compare(fa, fb);
            if (cmp != 0)
            {
                return cmp;
            }
        }
        return 0;
    }

    private static ref T GetRef<T>(in NameParts<T> parts, FirstNamePartIndex index)
    {
        ref var p = ref Unsafe.AsRef(in parts);
        return ref p.Ref(index);
    }

    public static ref T Ref<T>(this ref NameParts<T> n, FirstNamePartIndex index)
    {
        switch (index)
        {
            case FirstNamePartIndex.A:
                return ref n.A;
            case FirstNamePartIndex.B:
                return ref n.B;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public static T Get<T>(this NameParts<T> n, FirstNamePartIndex index)
    {
        return n.Ref(index);
    }

    public static NameParts<string?> Longer(this NameParts<OptionalFirstNamePart> name)
    {
        return name.Map(x => x.Longer);
    }
}

public static class NameDisplayHelper
{
    public struct TeacherNameParams()
    {
        public required StringBuilder Output;
        public required PersonName Name;
        public bool LastNameFirst = false;
        public bool PreferLonger = true;
        public bool InsertSpaceAfterShortName = true;
    }

    private enum WhichFirstName
    {
        None,
        Full,
        Short,
    }

    public static void Append(TeacherNameParams p)
    {
        var shouldAddSpaceNext = false;
        if (p.LastNameFirst)
        {
            if (AppendLastName())
            {
                shouldAddSpaceNext = true;
            }
            AppendFirstName();
        }
        else
        {
            var res = AppendFirstName();
            if (res == WhichFirstName.Full
                || res == WhichFirstName.Short && p.InsertSpaceAfterShortName)
            {
                shouldAddSpaceNext = true;
            }

            AppendLastName();
        }

        bool AppendLastName()
        {
            AppendSpaceMaybe();
            p.Output.Append(p.Name.LastName);
            return true;
        }
        WhichFirstName AppendFirstName()
        {
            var firstName = p.Name.FirstName;
            if (p.PreferLonger)
            {
                if (AppendLonger())
                {
                    return WhichFirstName.Full;
                }
                if (AppendShorter())
                {
                    return WhichFirstName.Short;
                }
                return WhichFirstName.None;
            }
            {
                if (AppendShorter())
                {
                    return WhichFirstName.Short;
                }
                if (AppendLonger())
                {
                    return WhichFirstName.Full;
                }
                return WhichFirstName.None;
            }

            bool AppendLonger()
            {
                if (firstName.A.Full is not { } a)
                {
                    return false;
                }

                if (firstName.B.Full is null
                    && firstName.B.Short is not null)
                {
                    return false;
                }

                AppendSpaceMaybe();

                var list = new ListStringBuilder(p.Output, separator: NameConstants.DoubleNameSeparator);
                list.Append(a);

                if (firstName.B.Full is { } b)
                {
                    list.Append(b);
                }

                return true;
            }
            bool AppendShorter()
            {
                if (firstName.A.Short is not { } a)
                {
                    return false;
                }

                AppendSpaceMaybe();

                var list = new ListStringBuilder(p.Output, separator: NameConstants.DoubleNameSeparator);

                {
                    var word = new WordSpan(a);
                    if (firstName.B.Short is not null)
                    {
                        // Skip the .
                        list.Append(word.Shortened.Value);
                    }
                    else
                    {
                        list.Append(word.Value);
                    }
                }

                if (firstName.B.Short is not { } b)
                {
                    return true;
                }
                list.Append(b);
                return true;
            }
        }

        void AppendSpaceMaybe()
        {
            if (shouldAddSpaceNext)
            {
                p.Output.Append(' ');
            }
        }
    }

}


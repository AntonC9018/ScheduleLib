using System.Runtime.CompilerServices;

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
public static class FirstNameHelper
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
            return ref FirstNameHelper.GetRef(parts, (FirstNamePartIndex) _value);
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

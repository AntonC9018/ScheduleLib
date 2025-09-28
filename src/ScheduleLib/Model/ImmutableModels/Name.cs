using System.Runtime.CompilerServices;
using System.Text;
using ScheduleLib.Parsing;

namespace ScheduleLib;

public record struct OptionalNamePart
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

    public override string ToString()
    {
        if (Full is null && Short is not null)
        {
            return Short;
        }
        var sb = new StringBuilder();
        sb.Append(Full);
        if (Short is not null)
        {
            sb.Append($" ({Short})");
        }
        return sb.ToString();
    }
}

[InlineArray((int) FirstNamePartIndex.Count)]
public struct NameParts<T>() : IEquatable<NameParts<T>>
{
    private T _items = default!;

    public bool Equals(NameParts<T> other)
    {
        return this.EachEquals(other, EqualityComparer<T>.Default.Equals);
    }

    public override bool Equals(object? obj)
    {
        return obj is NameParts<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in this)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(NameParts<T> left, NameParts<T> right) => left.Equals(right);
    public static bool operator !=(NameParts<T> left, NameParts<T> right) => !(left == right);

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append("[");
        var list = new ListStringBuilder(sb, ", ");
        foreach (var t in this)
        {
            if (t is not null)
            {
                list.Append($"{t}");
            }
        }
        sb.Append("]");
        return sb.ToString();
    }
}

public record struct LastName
{
    public NameParts<string?> Parts;

    public LastName() : this(default)
    {
    }

    public LastName(NameParts<string?> parts)
    {
        Parts = parts;
    }

    public static implicit operator LastName(string a)
    {
        var parts = default(NameParts<string?>);
        parts[0] = a;
        parts[1] = null;
        return new LastName(parts);
    }

    public static implicit operator NameParts<string?>(LastName n)
    {
        return n.Parts;
    }

    public readonly bool IsNull => Parts.All(x => x is null);

    public string? this[int index]
    {
        get => Parts[index];
        set => Parts[index] = value;
    }

    public override string ToString()
    {
        return Parts.ToString();
    }
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
        return ref n[(int) index];
    }

    public static NameParts<string?> Longer(this NameParts<OptionalNamePart> name)
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
                if (AppendLonger(firstName))
                {
                    return WhichFirstName.Full;
                }
                if (AppendShorter(firstName))
                {
                    return WhichFirstName.Short;
                }
                return WhichFirstName.None;
            }
            {
                if (AppendShorter(firstName))
                {
                    return WhichFirstName.Short;
                }
                if (AppendLonger(firstName))
                {
                    return WhichFirstName.Full;
                }
                return WhichFirstName.None;
            }

            bool AppendLonger(NameParts<OptionalNamePart> f)
            {
                if (f[0].Full is not { } a)
                {
                    return false;
                }

                if (f[1].Full is null
                    && f[1].Short is not null)
                {
                    return false;
                }

                AppendSpaceMaybe();

                var list = new ListStringBuilder(p.Output, separator: NameConstants.DoubleNameSeparator);
                list.Append(a);

                if (f[1].Full is { } b)
                {
                    list.Append(b);
                }

                return true;
            }
            bool AppendShorter(NameParts<OptionalNamePart> f)
            {
                if (f[0].Short is not { } a)
                {
                    return false;
                }

                AppendSpaceMaybe();

                var list = new ListStringBuilder(p.Output, separator: NameConstants.DoubleNameSeparator);

                {
                    var word = new WordSpan(a);
                    if (f[1].Short is not null)
                    {
                        // Skip the .
                        list.Append(word.Shortened.Value);
                    }
                    else
                    {
                        list.Append(word.Value);
                    }
                }

                if (f[1].Short is not { } b)
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


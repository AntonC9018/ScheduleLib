using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib;

public readonly record struct Indexed<T>(int Index, T Item);

public static class EnumerableHelper
{
    public static bool None<T>(this IEnumerable<T> source, Func<T, bool> pred)
    {
        return !source.Any(pred);
    }

    public static IEnumerable<Indexed<T>> WithIndex<T>(this IEnumerable<T> source)
    {
        return source.Select((x, i) => new Indexed<T>(i, x));
    }

    public static (T First, T Second)? MaybeJustTwoItems<T>(this IEnumerable<T> source)
    {
        if (source is IList<T> l)
        {
            int count = l.Count;
            switch (count)
            {
                case 2:
                    return (l[0], l[1]);
                default:
                    return null;
            }
        }
        {
            using var e = source.GetEnumerator();

            if (!e.MoveNext())
            {
                return null;
            }
            var first = e.Current;

            if (!e.MoveNext())
            {
                return null;
            }
            var second = e.Current;

            if (e.MoveNext())
            {
                return null;
            }
            return (first, second);
        }
    }

    public static (T First, T Second) JustTwoItems<T>(this IEnumerable<T> source)
    {
        if (source is IList<T> l)
        {
            int count = l.Count;
            switch (count)
            {
                case 0:
                    ThrowFirst();
                    break;
                case 1:
                    ThrowSecond();
                    break;
                case 2:
                    return (l[0], l[1]);
                default:
                    ThrowMore();
                    break;
            }
        }
        {
            using var e = source.GetEnumerator();

            if (!e.MoveNext())
            {
                ThrowFirst();
            }
            var first = e.Current;

            if (!e.MoveNext())
            {
                ThrowSecond();
            }
            var second = e.Current;

            if (e.MoveNext())
            {
                ThrowMore();
            }
            return (first, second);
        }

        [DoesNotReturn]
        void ThrowFirst()
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        [DoesNotReturn]
        void ThrowSecond()
        {
            throw new InvalidOperationException("Sequence contains only 1 element");
        }
        [DoesNotReturn]
        void ThrowMore()
        {
            throw new InvalidOperationException("Sequence contains more than 2 items");
        }
    }

    public static (T A, T B) At2<T>(this IEnumerable<T> source, int indexA, int indexB)
    {
        if (indexA >= indexB)
        {
            throw new ArgumentException("indexA must be less than indexB");
        }

        if (source is IList<T> l)
        {
            return (l[indexA], l[indexB]);
        }

        {
            using var e = source.GetEnumerator();
            int index = 0;
            var a = ItemAt(indexA);
            var b = ItemAt(indexB);
            return (a, b);


            T ItemAt(int i)
            {
                while (e.MoveNext())
                {
                    if (index == i)
                    {
                        return e.Current;
                    }
                    index++;
                }
                throw new InvalidOperationException("The list was too short");
            }
        }
    }

    public static RememberIsDoneEnumerator<T> RememberIsDone<T>(this IEnumerator<T> e) => new(e);
    public static RememberIsDoneEnumeratorClass<T> RememberIsDoneClass<T>(this IEnumerator<T> e) => new(e);

    public sealed class RememberIsDoneEnumeratorClass<T> : IEnumerator<T>
    {
        private RememberIsDoneEnumerator<T> _e;

        public RememberIsDoneEnumeratorClass(IEnumerator<T> e)
        {
            _e = new(e);
        }

        // TODO: [Forward]
        public bool IsDone => _e.IsDone;
        public bool MoveNext() => _e.MoveNext();
        public T Current => _e.Current;
        object? IEnumerator.Current => Current;
        public void Dispose() => _e.Dispose();
        public void Reset() => _e.Reset();
    }

    public struct RememberIsDoneEnumerator<T> : IDisposable
    {
        private readonly IEnumerator<T> _e;
        private bool _isDone;

        public RememberIsDoneEnumerator(IEnumerator<T> e)
        {
            _e = e;
        }

        public readonly bool IsDone => _isDone;
        public readonly T Current => _e.Current;
        public bool MoveNext()
        {
            if (!_e.MoveNext())
            {
                _isDone = true;
            }
            return !_isDone;
        }

        public void Dispose()
        {
            _e.Dispose();
        }

        public void Reset()
        {
            _e.Reset();
        }
    }

    public static bool IsSorted<T, P>(this IEnumerable<T> e, Func<T, P> item)
    {
        // ReSharper disable once PossibleMultipleEnumeration
        var ordered = e.OrderBy(item);
        // ReSharper disable once PossibleMultipleEnumeration
        bool ret = e.SequenceEqual(ordered);
        return ret;
    }

    public static IEnumerable<int> WhereSelectIndex<T>(this IEnumerable<T> t, Func<T, bool> pred)
    {
        int i = 0;
        foreach (var el in t)
        {
            if (pred(el))
            {
                yield return i;
            }
            i++;
        }
    }
}

public sealed class ClassEnumeratorWrapper<T, TEnumerator> : IEnumerator<T>
    where TEnumerator : struct, IEnumerator<T>
{
    private TEnumerator _enumerator;
    public ClassEnumeratorWrapper(TEnumerator enumerator)
    {
        _enumerator = enumerator;
    }
    public T Current => _enumerator.Current;
    object? IEnumerator.Current => _enumerator.Current;
    public void Dispose() => _enumerator.Dispose();
    public bool MoveNext() => _enumerator.MoveNext();
    public void Reset() => _enumerator.Reset();
    public TEnumerator EnumeratorState => _enumerator;
}

public static class EnumeratorHelper1
{
    public static ClassEnumeratorWrapper<T, TEnumerator> Wrap<T, TEnumerator>(
        this TEnumerator e,
        T? tag = default(T))

        where TEnumerator : struct, IEnumerator<T>
        where T : notnull
    {
        _ = tag;
        return new(e);
    }

    public static ClassEnumeratorWrapper<T, TEnumerator> WrapNullable<T, TEnumerator>(
        this TEnumerator e,
        T? tag = default(T))

        where TEnumerator : struct, IEnumerator<T>
    {
        _ = tag;
        return new(e);
    }
}

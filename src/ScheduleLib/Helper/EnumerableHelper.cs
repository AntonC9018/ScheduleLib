using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib;

public static class EnumerableHelper
{
    public static bool None<T>(this IEnumerable<T> source, Func<T, bool> pred)
    {
        return !source.Any(pred);
    }

    public static IEnumerable<(int Index, T Item)> WithIndex<T>(this IEnumerable<T> source)
    {
        return source.Select((x, i) => (i, x));
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
    }
}

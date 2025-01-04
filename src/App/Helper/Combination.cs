using System.Diagnostics;

namespace App;

// TODO: A non-recursive impl
#if false
public struct ArrangementEnumerator<T>
{
    private T[] _items;
    public T[] Current { get; }
    private BitArray32 _includedMask;

    public ArrangementEnumerator(T[] items, T[] current)
    {
        Current = current;
        _items = items;

        if (!BitArray32.CanCreate(items.Length))
        {
            throw new NotSupportedException("The max supported items is 32 currently");
        }

    }

    public bool MoveNext()
    {
        if (_includedMask.Length == 0)
        {
            _includedMask = BitArray32.NSet(_items.Length, Current.Length);
            if (_includedMask.Length == 0)
            {
                return false;
            }
            return true;
        }

        using var setIndexEnumerator = _includedMask.SetBitIndicesHighToLow.GetEnumerator();
        {
            bool s = setIndexEnumerator.MoveNext();
            Debug.Assert(s);
        }

        var lastIndex = setIndexEnumerator.Current;
        int currentLastIndex = _items.Length - 1;
        if (lastIndex == currentLastIndex)
        {
            while (setIndexEnumerator.MoveNext())
            {
                var prev = setIndexEnumerator.Current;
                currentLastIndex--;
                if (prev == currentLastIndex)
                {

                }
            }
        }

    }
}
#endif

public static class CombinationHelper
{
    public static IEnumerable<T[]> GenerateWithSingleOutputArray<T>(T[] items, int slots)
    {
        if (items.Length == 0)
        {
            return [];
        }
        if (slots == 0)
        {
            return [];
        }

        var resultMem = new T[slots];

        return Generate(items, resultMem);
    }

    public static IEnumerable<T[]> Generate<T>(T[] items, T[] resultMem)
    {
        foreach (var x in Generate(new ArraySegment<T>(items), new(resultMem)))
        {
            _ = x;
            yield return resultMem;
        }
    }

    public static IEnumerable<ArraySegment<T>> Generate<T>(ArraySegment<T> items, ArraySegment<T> resultMem)
    {
        if (resultMem.Count == 0)
        {
            yield return default;
            yield break;
        }
        else
        {
            Debug.Assert(items.Count > 0);
        }

        {
            resultMem[0] = items[0];
            var items1 = items[1 ..];
            var resultMem1 = resultMem[1 ..];
            foreach (var x1 in Generate(items1, resultMem1))
            {
                _ = x1;
                yield return resultMem;
            }
        }

        if (items.Count > resultMem.Count)
        {
            var items1 = items[1 ..];
            foreach (var x1 in Generate(items1, resultMem))
            {
                _ = x1;
                yield return resultMem;
            }
        }
    }
}

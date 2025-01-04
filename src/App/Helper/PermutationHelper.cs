using System.Buffers;
using System.Collections;

namespace App;

public static class PermutationHelper
{
    public static PermutationEnumerableWithCopy<T> GenerateWithSingleBackingCopy<T>(T[] elements) => new(elements);
    public static PermutationEnumerableWithoutCopy<T> Generate<T>(T[] inoutElements) => new(inoutElements);
}

public readonly struct PermutationEnumerableWithCopy<T>(T[] elems) : IEnumerable<T[]>
{
    public PermutationEnumerator<T> GetEnumerator()
    {
        var copy = elems.ToArray();
        return new(copy);
    }

    IEnumerator<T[]> IEnumerable<T[]>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public readonly struct PermutationEnumerableWithoutCopy<T>(T[] elems) : IEnumerable<T[]>
{
    public PermutationEnumerator<T> GetEnumerator() => new(elems);

    IEnumerator<T[]> IEnumerable<T[]>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}


public readonly record struct SwapAction(int Index0, int Index1);

// Heap's algorithm.
// https://www.wikiwand.com/en/articles/Heap%27s_algorithm
public struct PermutationActionEnumerator(int count) : IEnumerator<SwapAction>, IDisposable
{
    private int[] _stack = CreateStack(count);
    private int _index = 1;
    public SwapAction Current { get; private set; }

    public bool Initialized => _stack != null;

    private static int[] CreateStack(int size)
    {
        var ret = ArrayPool<int>.Shared.Rent(size);
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = 0;
        }
        return ret;
    }

    public bool MoveNext()
    {
        while (_index < count)
        {
            if (_stack[_index] < _index)
            {
                if ((_index & 1) == 0)
                {
                    Current = new(0, _index);
                }
                else
                {
                    Current = new(_stack[_index], _index);
                }
                _stack[_index]++;
                _index = 1;
                return true;
            }

            {
                _stack[_index] = 0;
                _index++;
            }
        }
        return false;
    }

    public void Dispose()
    {
        ArrayPool<int>.Shared.Return(_stack);
    }

    public void Reset() => throw new NotImplementedException();
    object? IEnumerator.Current => Current;
}

public struct PermutationEnumerator<T> : IEnumerator<T[]>, IDisposable
{
    public T[] Current { get; }
    private PermutationActionEnumerator _actionEnumerator;

    public PermutationEnumerator(T[] current)
    {
        Current = current;
        _actionEnumerator = default;
    }

    private void SwapAt(int a, int b)
    {
        (Current[a], Current[b]) = (Current[b], Current[a]);
    }

    public bool MoveNext()
    {
        if (!_actionEnumerator.Initialized)
        {
            _actionEnumerator = new(Current.Length);
            return true;
        }
        if (!_actionEnumerator.MoveNext())
        {
            return false;
        }
        var c = _actionEnumerator.Current;
        SwapAt(c.Index0, c.Index1);
        return true;
    }

    public void Dispose()
    {
        if (_actionEnumerator.Initialized)
        {
            _actionEnumerator.Dispose();
        }
    }

    public void Reset() => throw new NotImplementedException();
    object? IEnumerator.Current => Current;
}

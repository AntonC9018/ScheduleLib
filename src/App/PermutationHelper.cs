namespace App;

public static class PermutationHelper
{
    public static PermutationEnumerable<T> GenerateWithSingleBackingCopy<T>(T[] elems)
    {
        return new PermutationEnumerable<T>(elems);
    }
}

public struct PermutationEnumerable<T>(T[] elems)
{
    public PermutationEnumerator<T> GetEnumerator()
    {
        return new PermutationEnumerator<T>(elems.ToArray());
    }
}

public struct PermutationEnumerator<T>(T[] current)
{
    public T[] Current { get; } = current;
    private int[] _stack;
    private int _index = 1;

    private static int[] CreateStack(T[] elems)
    {
        var ret = new int[elems.Length - 1];
        for (int i = 0; i < ret.Length; i++)
        {
            ret[i] = 0;
        }
        return ret;
    }

    private void SwapAt(int a, int b)
    {
        (Current[a], Current[b]) = (Current[b], Current[a]);
    }

    public bool MoveNext()
    {
        if (_stack is null)
        {
            _stack = CreateStack(Current);
        }
        while (_index < Current.Length)
        {
            if (_stack[_index] < _index)
            {
                if ((_index & 1) == 0)
                {
                    SwapAt(0, _index);
                }
                else
                {
                    SwapAt(_stack[_index], _index);
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
}

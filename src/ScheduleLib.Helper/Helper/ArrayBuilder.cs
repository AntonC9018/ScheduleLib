using System.Diagnostics;

namespace ScheduleLib.Helper;

public struct ArrayBuilder<T>
{
    private int _index;
    private T[] Array { get; }

    public ArrayBuilder(T[] arr)
    {
        _index = 0;
        Array = arr;
    }

    public void Add(T item)
    {
        Debug.Assert(_index < Array.Length);
        Array[_index] = item;
        _index++;
    }

    public T[] Complete()
    {
        Debug.Assert(_index == Array.Length);
        return Array;
    }

    public Span<T> SoFar => Array.AsSpan(0, _index);
}

public static class ArrayBuilder
{
    public static ArrayBuilder<T> Create<T>(int count)
    {
        return new(new T[count]);
    }
}


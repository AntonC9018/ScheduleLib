using System.Diagnostics;

namespace ScheduleLib.Helper;

public ref struct SpanBuilderI<T>
{
    private ref int _index;
    private Span<T> Array { get; }

    public SpanBuilderI(Span<T> arr, ref int index)
    {
        _index = ref index;
        Array = arr;
    }

    public SpanBuilderI(ref SpanBuilder<T> b)
    {
        _index = ref b._index;
        Array = b.Array;
    }

    public void Add(T item)
    {
        Debug.Assert(_index < Array.Length);
        Array[_index] = item;
        _index++;
    }

    public Span<T> Complete()
    {
        Debug.Assert(_index == Array.Length);
        return Array;
    }

    public Span<T> SoFar => Array[.. _index];
}

public ref struct SpanBuilder<T>
{
    internal int _index;
    internal Span<T> Array { get; }

    public SpanBuilder(Span<T> arr)
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

    public Span<T> Complete()
    {
        Debug.Assert(_index == Array.Length);
        return Array;
    }

    public Span<T> SoFar => Array[.. _index];
}

public static class SpanBuilder
{
    public static SpanBuilder<T> Create<T>(Span<T> span)
    {
        return new(span);
    }
}

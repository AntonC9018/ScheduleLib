using System.Collections;

namespace ScheduleLib.Helper;

public sealed class SingleItemEnumerator<T> : IEnumerator<T>
{
    private T? _item;
    private bool _hasNext = false;

    public void Reset(T item)
    {
        _item = item;
        _hasNext = true;
    }

    object? IEnumerator.Current => Current;
    public T Current => _item!;

    public bool MoveNext()
    {
        if (_hasNext)
        {
            _hasNext = false;
            return true;
        }
        return false;
    }

    void IDisposable.Dispose()
    {
    }

    public void Reset()
    {
        _hasNext = false;
    }
}

public static class SingleItemEnumerator
{
    public static SingleItemEnumerator<T> Create<T>(T item)
    {
        var ret = new SingleItemEnumerator<T>();
        ret.Reset(item);
        return ret;
    }
}

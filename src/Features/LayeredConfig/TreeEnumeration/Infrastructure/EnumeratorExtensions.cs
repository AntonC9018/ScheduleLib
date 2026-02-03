using System.Collections;
using System.Diagnostics;

namespace Anton.LayeredConfig.TreeEnumeration.Infrastructure;

public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> e)
    {
        public SingleUseEnumerable<T> AsSingleUse() => new(e);

        public IEnumerable<T> DebugAssertSingleUse()
        {
            #if DEBUG
                return new DebugAssertSingleUseEnumerable<T>(e);
            #else
                return e.AsSingleUse();
            #endif
        }
    }
}

internal struct SingleUseItemHelper<T> where T : class
{
    private T? _i;
    public SingleUseItemHelper(T e) => _i = e;
    public T? Get() => Interlocked.Exchange(ref _i, null);
}

public struct SingleUseEnumerable<T> : IEnumerable<T>
{
    private SingleUseItemHelper<IEnumerable<T>> _helper;
    public SingleUseEnumerable(IEnumerable<T> e) => _helper = new(e);

    public IEnumerator<T> GetEnumerator()
    {
        if (_helper.Get() is not { } e)
        {
            throw new InvalidOperationException("This enumerable may only be enumerated once");
        }
        return e.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

#if DEBUG
public struct DebugAssertSingleUseEnumerable<T> : IEnumerable<T>
{
    private SingleUseItemHelper<IEnumerable<T>> _helper;
    public DebugAssertSingleUseEnumerable(IEnumerable<T> e) => _helper = new(e);

    public IEnumerator<T> GetEnumerator()
    {
        var e = _helper.Get();
        Debug.Assert(e != null, "Enumerable must only be enumerated once.");
        return e.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
#endif

using System.Buffers;
using System.Diagnostics;
using ScheduleLib.Helper;

namespace ScheduleLib;

public readonly struct RentedBuffer<T> : IDisposable
{
    public readonly T[] Array;
    public readonly int Len;

    public Span<T> Span => Array.AsSpan(0, Len);
    public Memory<T> Memory => Array.AsMemory(0, Len);

    public RentedBuffer(int len)
    {
        Array = ArrayPool<T>.Shared.Rent(len);
        Len = len;
    }

    public void Dispose()
    {
        Debug.Assert(IsValid);
        ArrayPool<T>.Shared.Return(Array);
    }

    public bool IsValid => Array != null;

    public SpanBuilder<T> Builder() => new(Span);
}

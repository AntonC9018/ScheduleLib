using System.Buffers;

namespace ScheduleLib;

public readonly struct RentedBuffer<T> : IDisposable
{
    public readonly T[] Array;
    public readonly int Length;

    public Span<T> Span => Array.AsSpan(0, Length);

    public RentedBuffer(int length)
    {
        Array = ArrayPool<T>.Shared.Rent(length);
        Length = length;
    }

    public void Dispose()
    {
        ArrayPool<T>.Shared.Return(Array);
    }
}

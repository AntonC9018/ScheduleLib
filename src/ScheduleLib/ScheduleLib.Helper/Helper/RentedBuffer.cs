using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ScheduleLib.Helper;

namespace ScheduleLib;

public readonly struct RentedBuffer<T> : IDisposable
{
    public readonly T[] Array;
    public readonly int Len;

    public Span<T> Span => Array.AsSpan(0, Len);
    public Memory<T> Memory => Array.AsMemory(0, Len);

    internal RentedBuffer(T[] arr, int len)
    {
        Array = arr;
        Len = len;
    }

    internal RentedBuffer(RentedBuffer<T> b, int len)
    {
        Debug.Assert(len <= b.Len);
        Array = b.Array;
        Len = len;
    }

    public RentedBuffer(int len)
    {
        Array = ArrayPool<T>.Shared.Rent(len);
        Len = len;
    }

    public void Dispose()
    {
        Debug.Assert(IsValid);
        ArrayPool<T>.Shared.Return(Array, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
    }

    public bool IsValid => Array != null;

    public SpanBuilder<T> Builder() => new(Span);

    public RentedBuffer<T> WithLen(int len) => new(this, len);
}

public static class RentedEnumerableExtensions
{
    extension<T>(IEnumerable<T> e)
    {
        public RentedBuffer<T> ToRentedBuffer()
        {
            if (e.TryGetNonEnumeratedCount(out var count))
            {
                var buffer = new RentedBuffer<T>(count);
                var i = 0;
                foreach (var item in e)
                {
                    buffer.Array[i++] = item;
                }

                return buffer;
            }

            var arr = ArrayPool<T>.Shared.Rent(16);
            var written = 0;
            try
            {
                foreach (var item in e)
                {
                    if (written == arr.Length)
                    {
                        var bigger = ArrayPool<T>.Shared.Rent(arr.Length * 2);
                        arr.AsSpan(0, written).CopyTo(bigger);
                        ArrayPool<T>.Shared.Return(arr);
                        arr = bigger;
                    }
                    arr[written++] = item;
                }
                return new RentedBuffer<T>(arr, written);
            }
            catch
            {
                ArrayPool<T>.Shared.Return(arr);
                throw;
            }
        }
    }
}


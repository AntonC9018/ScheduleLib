// https://github.com/AntonC9018/uni_csharp/blob/c28e8e3ce047a9cdede5ff825d51674609b75cd5/lab2_a/BitArray.cs#L17
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Numerics;
using System.Text;

public record struct BitArray64
{
    internal UnsizedBitArray64 _array;
    private readonly int _len;

    public readonly ulong Bits => _array.Bits;

    internal BitArray64(UnsizedBitArray64 array, int len)
    {
        UnsizedBitArray64.ValidateLen(len);
        Debug.Assert(array.Bits <= UnsizedBitArray64.GetMask(len).Bits);
        _array = array;
        _len = len;
    }

    public BitArray64(ulong bits, int len) :
        this(new UnsizedBitArray64(bits), len)
    {
    }
    [Pure]
    public readonly UnsizedBitArray64 AsUnsized() => _array;
    private readonly void ValidateIndex(int index)
    {
        Debug.Assert(index >= 0 && index < _len);
    }
    [Pure]
    public readonly int Len => _len;
    [Pure]
    public readonly int SetCount => _array.SetCount;

    public void Unset(int index)
    {
        ValidateIndex(index);
        _array.Set(index, false);
    }

    public void Set(int index, bool value = true)
    {
        ValidateIndex(index);
        _array.Set(index, value);
    }

    public void SetArray(BitArray64 arr, bool value = true)
    {
        Debug.Assert(arr.Len == Len);
        _array.SetArray(arr.AsUnsized(), value);
    }

    [Pure]
    public readonly bool IsSet(int index)
    {
        ValidateIndex(index);
        return _array.IsSet(index);
    }

    [Pure]
    public readonly bool IsSetArray(BitArray64 arr)
    {
        Debug.Assert(arr.Len == Len);
        return _array.IsSetArray(arr._array);
    }
    [Pure]
    public readonly int GetSetAfter(int index)
    {
        return _array.GetSetAfter(index);
    }
    [Pure]
    public readonly int GetUnsetAfter(int index)
    {
        Debug.Assert(index <= _len);
        return _array.GetUnsetAfter(index, _len);
    }
    [Pure]
    public readonly int GetUnsetAtOrAfter(int index)
    {
        Debug.Assert(index <= _len);
        return GetUnsetAfter(index - 1);
    }
    [Pure]
    public readonly BitArray64 WithSet(int index)
    {
        ValidateIndex(index);
        var s = _array.WithSet(index);
        return new(s, _len);
    }
    [Pure]
    public readonly BitArray64 WithClear(int index)
    {
        ValidateIndex(index);
        var s = _array.WithClear(index);
        return new(s, _len);
    }

    public void Clear(int index)
    {
        ValidateIndex(index);
        _array.Clear(index);
    }

    public void ClearArray(BitArray64 array)
    {
        Debug.Assert(array.Len == Len);
        _array.SetArray(array.AsUnsized(), false);
    }

    public void ClearAll()
    {
        _array = new(0);
    }

    [Pure]
    public readonly BitArray64 Flipped
    {
        get
        {
            var r = _array.Flipped(_len);
            return r;
        }
    }
    [Pure]
    public readonly bool CanSlice(int offset, int length)
    {
        ValidateIndex(offset);
        return offset + length <= _len;
    }
    [Pure]
    public readonly BitArray64 Slice(int offset, int length)
    {
        Debug.Assert(CanSlice(offset, length));

        var ret = _array.Slice(offset, length);
        return ret;
    }
    [Pure]
    public readonly BitArray64 Intersect(BitArray64 other)
    {
        Debug.Assert(other.Len == Len);
        var ret = _array.Intersect(other._array);
        return new(ret, _len);
    }
    [Pure]
    public readonly BitArray64 Remove(BitArray64 other)
    {
        Debug.Assert(other.Len == Len);
        var ret = _array.Remove(other._array);
        return new(ret, _len);
    }
    [Pure]
    public readonly BitArray64 IntersectAtOffset(BitArray64 other, int offset)
    {
        var slice = Slice(offset, other.Len);
        return slice.Intersect(other);
    }
    [Pure]
    public readonly bool AreAllSet => _array.AreAllSet(_len);
    [Pure]
    public readonly bool AreNoneSet => _array.AreNoneSet;
    [Pure]
    public readonly bool IsEmpty => _array.Bits == 0;

    [Pure]
    public static BitArray64 AllSet(int length)
    {
        var s = UnsizedBitArray64.AllSet(length);
        return new(s, length);
    }

    [Pure]
    public static BitArray64 NSet(int length, int set)
    {
        Debug.Assert(set <= length);
        var s = UnsizedBitArray64.GetMask(set);
        return new(s, length);
    }

    [Pure]
    public static BitArray64 Empty(int length)
    {
        return new(0, length);
    }

    [Pure]
    public readonly BitArray64 Union(BitArray64 other)
    {
        Debug.Assert(other.Len == Len);
        var ret = _array.UnionWith(other._array);
        return this with
        {
            _array = ret,
        };
    }

    [Pure]
    private readonly bool PrintMembers(StringBuilder sb)
    {
        sb.Append("Bits: ");
        sb.Append(_array.Bits);
        sb.Append(", Length: ");
        sb.Append(_len);
        return true;
    }

    // These would wrap custom enumerable structs if needed
    [Pure]
    public readonly SetBitIndicesEnumerable64 SetBitIndicesLowToHigh => new(_array.Bits);
    [Pure]
    public readonly ReverseSetBitIndicesEnumerable64 SetBitIndicesHighToLow => new(_array.Bits);
    [Pure]
    public readonly SetBitIndicesEnumerable64 UnsetBitIndicesLowToHigh => new(Flipped._array.Bits);
    [Pure]
    public readonly SlidingWindowLowToHighEnumerable64 SlidingWindowLowToHigh(int length) => new(this, length);

    [Pure]
    public readonly Interval SetBitInterval
    {
        get
        {
            var firstSetBit = SetBitIndicesLowToHigh.First();
            var lastSetBit = SetBitIndicesHighToLow.First();
            return new Interval(firstSetBit, lastSetBit);
        }
    }

    public const int MaxLength = sizeof(ulong) * 8;
}


public readonly struct ReverseSetBitIndicesEnumerable64 : IEnumerable<int>
{
    private readonly ulong _bits;

    public ReverseSetBitIndicesEnumerable64(ulong bits)
    {
        _bits = bits;
    }

    public ReverseSetBitIndicesEnumerator64 GetEnumerator() => new(_bits);
    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    [Pure]
    public readonly int Single() => NonAllocEnumerable.Single<int, ReverseSetBitIndicesEnumerator64>(GetEnumerator());
    [Pure]
    public readonly int First() => NonAllocEnumerable.First<int, ReverseSetBitIndicesEnumerator64>(GetEnumerator());
}

public struct ReverseSetBitIndicesEnumerator64 : IEnumerator<int>
{
    private ulong _bits;
    private int _current;

    public ReverseSetBitIndicesEnumerator64(ulong bits)
    {
        _bits = bits;
        _current = 0;
    }

    public bool MoveNext()
    {
        if (_bits == 0)
        {
            return false;
        }

        _current = (sizeof(ulong) * 8) - BitOperations.LeadingZeroCount(_bits) - 1;
        _bits &= ~(1UL << _current);
        return true;
    }

    public readonly int Current => _current;

    readonly object IEnumerator.Current => Current;

    public void Reset()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
    }
}

public readonly struct SetBitIndicesEnumerable64 : IEnumerable<int>
{
    private readonly ulong _bits;

    public SetBitIndicesEnumerable64(ulong bits) => _bits = bits;
    public SetBitIndicesEnumerator64 GetEnumerator() => new(_bits);
    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Single() => NonAllocEnumerable.Single<int, SetBitIndicesEnumerator64>(GetEnumerator());
    public int First() => NonAllocEnumerable.First<int, SetBitIndicesEnumerator64>(GetEnumerator());
}

public struct SetBitIndicesEnumerator64 : IEnumerator<int>
{
    private ulong _bits;
    private int _current;

    public SetBitIndicesEnumerator64(ulong bits)
    {
        _bits = bits;
        _current = 0;
    }

    public bool MoveNext()
    {
        if (_bits == 0)
        {
            return false;
        }

        _current = BitOperations.TrailingZeroCount(_bits);
        _bits &= ~(1UL << _current);
        return true;
    }

    public readonly int Current => _current;

    readonly object IEnumerator.Current => Current;

    public void Reset()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
    }
}

public readonly record struct SliceAndOffset64(int Offset, BitArray64 Slice);

public readonly struct SlidingWindowLowToHighEnumerable64 : IEnumerable<SliceAndOffset64>
{
    private readonly BitArray64 _source;
    private readonly int _length;

    public SlidingWindowLowToHighEnumerable64(BitArray64 source, int length)
    {
        _source = source;
        _length = length;
    }

    public struct Enumerator : IEnumerator<SliceAndOffset64>
    {
        private SlidingWindowLowToHighEnumerable64 _e;
        private int _offset;

        public Enumerator(SlidingWindowLowToHighEnumerable64 e)
        {
            _e = e;
            _offset = -1;
        }

        public bool MoveNext()
        {
            _offset++;

            if (!_e._source.CanSlice(_offset, _e._length))
            {
                return false;
            }
            return true;
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        object IEnumerator.Current => Current;

        public SliceAndOffset64 Current => new(_offset, _e._source.Slice(_offset, _e._length));

        public void Dispose()
        {
        }
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<SliceAndOffset64> IEnumerable<SliceAndOffset64>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

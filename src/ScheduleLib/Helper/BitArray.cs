// https://github.com/AntonC9018/uni_csharp/blob/c28e8e3ce047a9cdede5ff825d51674609b75cd5/lab2_a/BitArray.cs#L17
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Text;

public readonly struct Interval
{
    public readonly int Start;
    public readonly int EndInclusive;

    public Interval(int start, int endInclusive)
    {
        Start = start;
        EndInclusive = endInclusive;
    }

    public int Length => EndInclusive - Start + 1;
}

public record struct UnsizedBitArray32
{
    private uint _bits;

    public UnsizedBitArray32(uint bits)
    {
        _bits = bits;
    }

    public BitArray32 WithFixedSize(int size) => new(this, size);

    public readonly int SetCount => BitOperations.PopCount(_bits);

    private static void ValidateIndex(int index)
    {
        Debug.Assert(index <= BitArray32.MaxLength);
    }
    internal static void ValidateLength(int len)
    {
        Debug.Assert(len <= BitArray32.MaxLength);
    }

    public void Set(int index, bool value)
    {
        ValidateIndex(index);

        if (value)
        {
            Set(index);
        }
        else
        {
            Clear(index);
        }
    }

    public void Set(int index)
    {
        ValidateIndex(index);
        _bits |= (1u << index);
    }

    public void Clear(int index)
    {
        ValidateIndex(index);
        _bits &= ~(1u << index);
    }

    public readonly bool IsSet(int index)
    {
        ValidateIndex(index);
        return (_bits & (1u << index)) != 0;
    }

    public readonly int GetSetAfter(int index)
    {
        ValidateIndex(index);

        var ignoredMask = index < 0 ? 0 : GetMask(index + 1);
        var set = _bits & ~ignoredMask;
        if (set == 0)
        {
            return -1;
        }

        return BitOperations.TrailingZeroCount(set);
    }

    public readonly int GetUnsetAfter(int index, int length)
    {
        ValidateIndex(index);
        Debug.Assert(index < length);

        if (length == 0)
        {
            return -1;
        }

        var ignoredMask = index < 0 ? 0 : GetMask(index + 1);
        var allMask = GetMask(length);
        var mask = ~ignoredMask & allMask;
        var unset = ~_bits & mask;
        if (unset == 0)
        {
            return -1;
        }

        return BitOperations.TrailingZeroCount(unset);
    }

    public readonly UnsizedBitArray32 WithSet(int index)
    {
        ValidateIndex(index);

        var copy = this;
        copy.Set(index);
        return copy;
    }

    public readonly UnsizedBitArray32 WithClear(int index)
    {
        ValidateIndex(index);

        var copy = this;
        copy.Clear(index);
        return copy;
    }

    public readonly UnsizedBitArray32 Flipped(int length)
    {
        var not = (~_bits) & GetMask(length);
        return new(not);
    }

    public readonly UnsizedBitArray32 Slice(int offset, int length)
    {
        ValidateIndex(offset);
        var bits = _bits >> offset;
        var mask = GetMask(length);
        return new(bits & mask);
    }

    public readonly UnsizedBitArray32 Intersect(UnsizedBitArray32 other)
    {
        return new(_bits & other._bits);
    }

    public readonly UnsizedBitArray32 Intersect(UnsizedBitArray32 other, int length)
    {
        var r = Intersect(other);
        return new(r._bits & GetMask(length));
    }

    public readonly bool AreAllSet(int length) => _bits == GetMask(length);
    public readonly bool AreNoneSet => _bits == 0;

    public readonly uint Bits => _bits;

    public static UnsizedBitArray32 AllSet(int length = BitArray32.MaxLength)
    {
        var s = GetMask(length);
        return new(s);
    }

    public static uint GetMask(int length)
    {
        ValidateLength(length);

        if (length == 0)
        {
            return 0;
        }

        int shift = sizeof(uint) * 8 - length;
        return ~default(uint) >> shift;
    }
}

public record struct BitArray32
{
    private UnsizedBitArray32 _array;
    private readonly int _length;

    internal BitArray32(UnsizedBitArray32 array, int length)
    {
        UnsizedBitArray32.ValidateLength(length);
        Debug.Assert(array.Bits <= UnsizedBitArray32.GetMask(length));
        _array = array;
        _length = length;
    }

    public BitArray32(uint bits, int length) :
        this(new UnsizedBitArray32(bits), length)
    {
    }

    private readonly void ValidateIndex(int index)
    {
        Debug.Assert(index >= 0 && index < _length);
    }

    public readonly int Length => _length;

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

    public readonly bool IsSet(int index)
    {
        ValidateIndex(index);
        return _array.IsSet(index);
    }

    public readonly int GetSetAfter(int index)
    {
        ValidateIndex(index);
        return _array.GetSetAfter(index);
    }

    public readonly int GetUnsetAfter(int index)
    {
        Debug.Assert(index <= _length);
        return _array.GetUnsetAfter(index, _length);
    }

    public readonly int GetUnsetAtOrAfter(int index)
    {
        Debug.Assert(index <= _length);
        return GetUnsetAfter(index - 1);
    }

    public readonly BitArray32 WithSet(int index)
    {
        ValidateIndex(index);
        var s = _array.WithSet(index);
        return new(s, _length);
    }

    public readonly BitArray32 WithClear(int index)
    {
        ValidateIndex(index);
        var s = _array.WithClear(index);
        return new(s, _length);
    }

    public void Clear(int index)
    {
        ValidateIndex(index);
        _array.Clear(index);
    }

    public void ClearAll()
    {
        _array = new UnsizedBitArray32(0);
    }

    public readonly BitArray32 Flipped
    {
        get
        {
            var r = _array.Flipped(_length);
            return new(r, _length);
        }
    }

    public readonly bool CanSlice(int offset, int length)
    {
        ValidateIndex(offset);
        return offset + length <= _length;
    }

    public readonly BitArray32 Slice(int offset, int length)
    {
        Debug.Assert(CanSlice(offset, length));

        var ret = _array.Slice(offset, length);
        return new(ret, length);
    }

    public readonly BitArray32 Intersect(BitArray32 other)
    {
        Debug.Assert(other.Length == Length);
        var ret = _array.Intersect(other._array, _length);
        return new(ret, _length);
    }

    public readonly BitArray32 IntersectAtOffset(BitArray32 other, int offset)
    {
        var slice = Slice(offset, other.Length);
        return slice.Intersect(other);
    }

    public readonly bool AreAllSet => _array.AreAllSet(_length);
    public readonly bool AreNoneSet => _array.AreNoneSet;

    public readonly bool IsEmpty => _array.Bits == 0;

    public static BitArray32 AllSet(int length)
    {
        var s = UnsizedBitArray32.AllSet(length);
        return new(s, length);
    }

    public static BitArray32 NSet(int length, int set)
    {
        Debug.Assert(set <= length);
        var s = UnsizedBitArray32.GetMask(set);
        return new(s, length);
    }

    public static BitArray32 Empty(int length)
    {
        return new(0, length);
    }

    private readonly bool PrintMembers(StringBuilder sb)
    {
        sb.Append("Bits: ");
        sb.Append(_array.Bits);
        sb.Append(", Length: ");
        sb.Append(_length);
        return true;
    }

    // These would wrap custom enumerable structs if needed
    public readonly SetBitIndicesEnumerable SetBitIndicesLowToHigh => new(_array.Bits);
    public readonly ReverseSetBitIndicesEnumerable SetBitIndicesHighToLow => new(_array.Bits);
    public readonly SetBitIndicesEnumerable UnsetBitIndicesLowToHigh => new(Flipped._array.Bits);
    public readonly SlidingWindowLowToHighEnumerable SlidingWindowLowToHigh(int length) => new(this, length);

    public readonly Interval SetBitInterval
    {
        get
        {
            var firstSetBit = SetBitIndicesLowToHigh.First();
            var lastSetBit = SetBitIndicesHighToLow.First();
            return new Interval(firstSetBit, lastSetBit);
        }
    }

    public const int MaxLength = sizeof(uint) * 8;
}

public readonly struct ReverseSetBitIndicesEnumerable : IEnumerable<int>
{
    private readonly uint _bits;

    public ReverseSetBitIndicesEnumerable(uint bits)
    {
        _bits = bits;
    }

    public ReverseSetBitIndicesEnumerator GetEnumerator() => new(_bits);
    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Single() => NonAllocEnumerable.Single<int, ReverseSetBitIndicesEnumerator>(GetEnumerator());
    public int First() => NonAllocEnumerable.First<int, ReverseSetBitIndicesEnumerator>(GetEnumerator());
}

public struct ReverseSetBitIndicesEnumerator : IEnumerator<int>
{
    private uint _bits;
    private int _current;

    public ReverseSetBitIndicesEnumerator(uint bits)
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

        _current = (sizeof(uint) * 8) - BitOperations.LeadingZeroCount(_bits) - 1;
        _bits &= ~(1u << _current);
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

public readonly struct SetBitIndicesEnumerable : IEnumerable<int>
{
    private readonly uint _bits;

    public SetBitIndicesEnumerable(uint bits)
    {
        _bits = bits;
    }

    public SetBitIndicesEnumerator GetEnumerator() => new(_bits);
    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Single() => NonAllocEnumerable.Single<int, SetBitIndicesEnumerator>(GetEnumerator());
    public int First() => NonAllocEnumerable.First<int, SetBitIndicesEnumerator>(GetEnumerator());
}

public struct SetBitIndicesEnumerator : IEnumerator<int>
{
    private uint _bits;
    private int _current;

    public SetBitIndicesEnumerator(uint bits)
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
        _bits &= ~(1u << _current);
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

public readonly record struct SliceAndOffset(int Offset, BitArray32 Slice);

public readonly struct SlidingWindowLowToHighEnumerable : IEnumerable<SliceAndOffset>
{
    private readonly BitArray32 _source;
    private readonly int _length;

    public SlidingWindowLowToHighEnumerable(BitArray32 source, int length)
    {
        _source = source;
        _length = length;
    }

    public struct Enumerator : IEnumerator<SliceAndOffset>
    {
        private SlidingWindowLowToHighEnumerable _e;
        private int _offset;

        public Enumerator(SlidingWindowLowToHighEnumerable e)
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

        public SliceAndOffset Current => new(_offset, _e._source.Slice(_offset, _e._length));

        public void Dispose()
        {
        }
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<SliceAndOffset> IEnumerable<SliceAndOffset>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

file static class NonAllocEnumerable
{
    public static T? First<T, E>(E e) where E : IEnumerator<T>
    {
        using var enumerator = e;
        if (!enumerator.MoveNext())
        {
            return default;
        }
        return enumerator.Current;
    }

    public static T Single<T, E>(E e) where E : IEnumerator<T>
    {
        using var enumerator = e;
        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException("No elements");
        }
        var r = enumerator.Current;
        if (enumerator.MoveNext())
        {
            throw new InvalidOperationException("More than one element");
        }
        return r;
    }
}

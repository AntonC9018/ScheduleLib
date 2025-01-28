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

public record struct BitArray32
{
    private uint _bits;
    private readonly int _length;

    private BitArray32(uint bits, int length)
    {
        Debug.Assert(length <= sizeof(uint) * 8);
        _bits = bits;
        _length = length;
    }

    public readonly int Length => _length;

    public readonly int SetCount => BitOperations.PopCount(_bits);

    public void Set(int index, bool value = true)
    {
        Debug.Assert(index < _length);
        if (value)
        {
            _bits |= (1u << index);
        }
        else
        {
            Clear(index);
        }
    }

    public readonly bool IsSet(int index)
    {
        Debug.Assert(index < _length);
        return (_bits & (1u << index)) != 0;
    }

    public readonly BitArray32 WithSet(int index)
    {
        var r = this;
        r.Set(index);
        return r;
    }

    public void Clear(int index)
    {
        Debug.Assert(index < _length);
        _bits &= ~(1u << index);
    }

    public readonly BitArray32 Flipped
    {
        get
        {
            var not = (~_bits) & GetMask(_length);
            return new(not, _length);
        }
    }

    public readonly bool CanSlice(int offset, int length)
    {
        return offset + length <= _length;
    }
    public readonly BitArray32 Slice(int offset, int length)
    {
        Debug.Assert(CanSlice(offset, length));
        var bits = _bits >> offset;
        var mask = GetMask(length);
        return new(bits & mask, length);
    }

    public readonly BitArray32 Intersect(BitArray32 other)
    {
        Debug.Assert(other.Length == Length);
        var and = other._bits & _bits;
        Debug.Assert((GetMask(other._length) & and) == and);
        return new BitArray32(and, other._length);
    }

    public readonly BitArray32 IntersectAtOffset(BitArray32 other, int offset)
    {
        var slice = Slice(offset, other.Length);
        var ret = other.Intersect(slice);
        return ret;
    }

    public readonly bool AreAllSet => _bits == GetMask(_length);
    public readonly bool AreNoneSet => _bits == 0;

    public readonly BitArray32 WithClear(int index)
    {
        var r = this;
        r.Clear(index);
        return r;
    }

    public readonly Interval SetBitInterval
    {
        get
        {
            var firstSetBit = SetBitIndicesLowToHigh.First();
            var lastSetBit = SetBitIndicesHighToLow.First();
            return new Interval(firstSetBit, lastSetBit);
        }
    }
    public readonly SetBitIndicesEnumerable SetBitIndicesLowToHigh => new(_bits);
    public readonly ReverseSetBitIndicesEnumerable SetBitIndicesHighToLow => new(_bits);
    public readonly SetBitIndicesEnumerable UnsetBitIndicesLowToHigh => new(Flipped._bits);
    public readonly SlidingWindowLowToHighEnumerable SlidingWindowLowToHigh(int length) => new(this, length);

    public bool IsEmpty => _bits == 0;

    public static BitArray32 AllSet(int length)
    {
        return new BitArray32(GetMask(length), length);
    }

    public static BitArray32 NSet(int length, int set)
    {
        Debug.Assert(set <= length);
        return new BitArray32(GetMask(set), length);
    }

    private static uint GetMask(int length)
    {
        return ~default(uint) >> (sizeof(uint) * 8 - length);
    }

    public static BitArray32 Empty(int length)
    {
        return new BitArray32(default, length);
    }

    public static bool CanCreate(int length)
    {
        return length <= MaxLength;
    }

    public const int MaxLength = sizeof(uint) * 8;

    private bool PrintMembers(StringBuilder sb)
    {
        sb.Append("Bits: ");
        sb.Append(_bits);
        sb.Append(", Length: ");
        sb.Append(_length);
        return true;
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

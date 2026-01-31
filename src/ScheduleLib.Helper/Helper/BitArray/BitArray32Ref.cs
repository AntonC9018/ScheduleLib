using System.Diagnostics;

public ref struct SizedBitArray32Ref
{
    private ref UnsizedBitArray32 _array;
    private readonly int _length;

    public SizedBitArray32Ref(ref UnsizedBitArray32 array, int length)
    {
        UnsizedBitArray32.ValidateLength(length);
        _array = ref array;
        _length = length;
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

    public void Clear(int index)
    {
        ValidateIndex(index);
        _array.Clear(index);
    }

    public void ClearAll()
    {
        _array = new UnsizedBitArray32(0);
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

    public readonly bool AreAllSet => _array.AreAllSet(_length);
    public readonly bool AreNoneSet => _array.AreNoneSet;

    public readonly bool IsEmpty => _array.Bits == 0;

    public readonly uint Bits => _array.Bits;

    public readonly SetBitIndicesEnumerable SetBitIndicesLowToHigh => new(_array.Bits);
    public readonly ReverseSetBitIndicesEnumerable SetBitIndicesHighToLow => new(_array.Bits);

    public readonly SetBitIndicesEnumerable UnsetBitIndicesLowToHigh
    {
        get
        {
            var flipped = _array.Flipped(_length);
            return new(flipped.Bits);
        }
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

    public readonly BitArray32 ToBitArray32()
    {
        return new BitArray32(_array.Bits, _length);
    }
}

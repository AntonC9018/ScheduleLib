using System.Diagnostics;

public ref struct SizedBitArray64Ref
{
    private ref UnsizedBitArray64 _array;
    private readonly int _length;

    public SizedBitArray64Ref(ref UnsizedBitArray64 array, int length)
    {
        UnsizedBitArray64.ValidateLen(length);
        _array = ref array;
        _length = length;
    }

    public static SizedBitArray64Ref Create(ref BitArray64 arr, int len)
    {
        Debug.Assert(arr.Len <= len);
        return new(ref arr._array, len);
    }

    private readonly void ValidateIndex(int index)
    {
        Debug.Assert(index >= 0 && index < _length);
    }

    public readonly int Len => _length;

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
        _array = new UnsizedBitArray64(0);
    }

    public readonly bool CanSlice(int offset, int length)
    {
        ValidateIndex(offset);
        return offset + length <= _length;
    }

    public readonly BitArray64 Slice(int offset, int length)
    {
        Debug.Assert(CanSlice(offset, length));
        var ret = _array.Slice(offset, length);
        return ret;
    }

    public void Union(BitArray64 other)
    {
        Debug.Assert(Len == other.Len);
        _array = _array.UnionWith(other.AsUnsized());
    }

    public readonly bool AreAllSet => _array.AreAllSet(_length);
    public readonly bool AreNoneSet => _array.AreNoneSet;

    public readonly bool IsEmpty => _array.Bits == 0;

    public readonly ulong Bits => _array.Bits;

    public readonly SetBitIndicesEnumerable64 SetBitIndicesLowToHigh => new(_array.Bits);
    public readonly ReverseSetBitIndicesEnumerable64 SetBitIndicesHighToLow => new(_array.Bits);

    public readonly SetBitIndicesEnumerable64 UnsetBitIndicesLowToHigh
    {
        get
        {
            var flipped = _array.Flipped(_length);
            return new(flipped._array.Bits);
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

    public readonly BitArray64 ToBitArray64()
    {
        return new BitArray64(_array.Bits, _length);
    }
}

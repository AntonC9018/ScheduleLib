using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Numerics;

public record struct UnsizedBitArray32
{
    private uint _bits;

    public UnsizedBitArray32(uint bits)
    {
        _bits = bits;
    }

    [Pure]
    public readonly BitArray32 WithFixedSize(int size) => new(this, size);
    [Pure]
    public readonly int SetCount => BitOperations.PopCount(_bits);

    private static void ValidateIndex(int index)
    {
        if (index < 0 || index >= BitArray32.MaxLength)
        {
            Debug.Fail($"Index {index} is invalid");
        }
    }
    internal static void ValidateLen(int len)
    {
        if (len < 0 || len > BitArray32.MaxLength)
        {
            Debug.Fail($"Len {len} is invalid");
        }
    }

    private static UnsizedBitArray32 IndexMask(int i)
    {
        ValidateIndex(i);
        return new(1u << i);
    }

    public void Set(int index, bool value)
    {
        var mask = IndexMask(index);
        SetArray(mask, value);
    }

    public void SetArray(UnsizedBitArray32 arr, bool value)
    {
        if (value)
        {
            SetArray(arr);
        }
        else
        {
            ClearArray(arr);
        }
    }

    public void SetArray(UnsizedBitArray32 array)
    {
        _bits |= array._bits;
    }

    public void Set(int index) => Set(index, value: true);

    public void ClearArray(UnsizedBitArray32 other)
    {
        _bits &= ~(other._bits);
    }

    public void Clear(int index)
    {
        var mask = IndexMask(index);
        ClearArray(mask);
    }
    [Pure]
    public readonly bool IsSet(int index)
    {
        ValidateIndex(index);
        return (_bits & (1u << index)) != 0;
    }

    [Pure]
    public readonly bool IsSetArray(UnsizedBitArray32 arr)
    {
        return Intersect(arr) == arr;
    }
    [Pure]
    public readonly int GetSetAfter(int index)
    {
        Debug.Assert(index < BitArray32.MaxLength);

        var ignoredMask = index < 0 ? 0 : GetMask(index + 1).Bits;
        var set = _bits & ~ignoredMask;
        if (set == 0)
        {
            return -1;
        }

        return BitOperations.TrailingZeroCount(set);
    }
    [Pure]
    public readonly int GetUnsetAfter(int index, int length)
    {
        Debug.Assert(index < length);

        if (length == 0)
        {
            return -1;
        }

        var ignoredMask = index < 0 ? 0 : GetMask(index + 1).Bits;
        var allMask = GetMask(length).Bits;
        var mask = ~ignoredMask & allMask;
        var unset = ~_bits & mask;
        if (unset == 0)
        {
            return -1;
        }

        return BitOperations.TrailingZeroCount(unset);
    }
    [Pure]
    public readonly UnsizedBitArray32 WithSet(int index)
    {
        ValidateIndex(index);

        var copy = this;
        copy.Set(index);
        return copy;
    }
    [Pure]
    public readonly UnsizedBitArray32 WithClear(int index)
    {
        ValidateIndex(index);

        var copy = this;
        copy.Clear(index);
        return copy;
    }
    [Pure]
    public readonly UnsizedBitArray32 Flipped()
    {
        var not = ~_bits;
        return new(not);
    }
    [Pure]
    public readonly BitArray32 Flipped(int length)
    {
        var x = this;
        return x.Flipped().WithShrunkLen(length);
    }

    [Pure]
    public readonly BitArray32 WithShrunkLen(int len)
    {
        var x = this;
        x = x.Intersect(GetMask(len));
        var ret = x.WithFixedSize(len);
        return ret;
    }

    [Pure]
    public readonly BitArray32 Slice(int offset, int length)
    {
        ValidateIndex(offset);
        var bits = _bits >> offset;
        var mask = GetMask(length);
        return new(bits & mask.Bits, length);
    }
    [Pure]
    public readonly UnsizedBitArray32 Intersect(UnsizedBitArray32 other)
    {
        return new(_bits & other._bits);
    }
    [Pure]
    public readonly UnsizedBitArray32 Remove(UnsizedBitArray32 other)
    {
        var r = Intersect(other.Flipped());
        return r;
    }

#if false
    [Pure]
    public readonly UnsizedBitArray32 Shifted(int amount)
    {
        if (amount == 0)
        {
            return this;
        }
        if (amount > 0)
        {
            return ShiftedLeft(amount);
        }
        return ShiftedRight(-amount);
    }
#endif

    [Pure]
    public readonly UnsizedBitArray32 ShiftedRight(int offset)
    {
        int firstSet = SetBitIndicesLowToHigh.First();
        int firstNewPos = firstSet - offset;
        ValidateIndex(firstNewPos);
        var ret = ShiftedRightWithDataLoss(offset);
        return ret;
    }

    [Pure]
    public readonly UnsizedBitArray32 ShiftedRightWithDataLoss(int offset)
    {
        var s = Bits >> offset;
        return new(s);
    }

    [Pure]
    public readonly UnsizedBitArray32 ShiftedLeft(int offset)
    {
        int lastSet = SetBitIndicesHighToLow.First();
        int lastsNewPos = lastSet + offset;
        ValidateIndex(lastsNewPos);

        var s = Bits << offset;
        return new(s);
    }

    [Pure]
    public readonly bool AreAllSet(int length)
    {
        ValidateLen(length);
        var mask = GetMask(length);
        return Intersect(mask) == mask;
    }

    [Pure]
    public readonly bool AreNoneSet => _bits == 0;
    [Pure]
    public readonly uint Bits => _bits;
    [Pure]
    public readonly bool IsEmpty => AreNoneSet;

    public static UnsizedBitArray32 AllSet(int length = BitArray32.MaxLength)
    {
        var s = GetMask(length);
        return s;
    }

    public static UnsizedBitArray32 GetMask(int length)
    {
        ValidateLen(length);

        if (length == 0)
        {
            return new(0);
        }

        int shift = sizeof(uint) * 8 - length;
        return new(~default(uint) >> shift);
    }

    public static UnsizedBitArray32 GetOffsetMask(int offset, int length)
    {
        Debug.Assert(offset >= 0);
        ValidateLen(length);
        Debug.Assert(offset + length <= BitArray32.MaxLength);

        var mask = GetMask(length);
        return new(mask.Bits << offset);
    }

    [Pure]
    public readonly UnsizedBitArray32 Union(UnsizedBitArray32 otherArray)
    {
        return new(otherArray.Bits | Bits);
    }

    // These would wrap custom enumerable structs if needed
    [Pure]
    public readonly SetBitIndicesEnumerable SetBitIndicesLowToHigh => new(Bits);
    [Pure]
    public readonly ReverseSetBitIndicesEnumerable SetBitIndicesHighToLow => new(Bits);
    [Pure]
    public readonly SetBitIndicesEnumerable UnsetBitIndicesLowToHigh => new(Flipped().Bits);
}

public static class UnsizedBitArrayHelper
{
    public static SizedBitArray32Ref AsFixedSizeRef(this ref UnsizedBitArray32 arr, int size)
    {
        return new SizedBitArray32Ref(ref arr, size);
    }
}

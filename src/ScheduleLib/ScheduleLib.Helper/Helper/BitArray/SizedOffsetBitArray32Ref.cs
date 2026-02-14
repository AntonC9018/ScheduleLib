using System.Diagnostics;

public ref struct SizedOffsetBitArray32Ref
{
    private ref UnsizedBitArray32 _array;
    public readonly int Offset;
    private readonly int _length;

    public SizedOffsetBitArray32Ref(
        ref UnsizedBitArray32 array,
        int offset,
        int length)
    {
        Debug.Assert(offset >= 0);
        Debug.Assert(length >= 0);
        Debug.Assert(offset + length <= BitArray32.MaxLength);

        _array = ref array;
        Offset = offset;
        _length = length;
    }

    public static SizedOffsetBitArray32Ref Create(
        ref BitArray32 arr,
        int offset,
        int len)
    {
        Debug.Assert(offset + len <= arr.Len);
        return new SizedOffsetBitArray32Ref(
            ref arr._array,
            offset: offset,
            length: len);
    }

    private readonly void ValidateIndex(int index)
    {
        Debug.Assert(index >= 0 && index < _length);
    }

    private readonly void ValidateMask(UnsizedBitArray32 mask)
    {
        Debug.Assert(mask.WithShrunkLen(_length).AsUnsized() == mask);
    }

    private readonly int RestoreIndex(int index)
    {
        ValidateIndex(index);
        return Offset + index;
    }

    public readonly int Length => _length;

    private readonly BitArray32 Slice => _array.Slice(Offset, _length);

    public readonly int SetCount => Slice.SetCount;

    public void Unset(int index)
    {
        int i = RestoreIndex(index);
        _array.Set(i, false);
    }

    public void Set(int index, bool value = true)
    {
        int i = RestoreIndex(index);
        _array.Set(i, value);
    }

    public readonly bool IsSet(int index)
    {
        int i = RestoreIndex(index);
        return _array.IsSet(i);
    }

    public void Clear(int index)
    {
        int i = RestoreIndex(index);
        _array.Clear(i);
    }

    public void ClearAll()
    {
        var shiftedMask = UnsizedBitArray32.GetOffsetMask(Offset, _length);
        _array.ClearArray(shiftedMask);
    }

    public void Clear(UnsizedBitArray32 mask)
    {
        ValidateMask(mask);
        var shiftedMask = mask.ShiftedLeft(Offset);
        _array.ClearArray(shiftedMask);
    }

    public readonly bool AreAllSet => Slice.AreAllSet;
    public readonly bool AreNoneSet => Slice.AreNoneSet;
    public readonly bool IsEmpty => Slice.IsEmpty;

    public void SetArray(UnsizedBitArray32 array)
    {
        ValidateMask(array);
        var x = array.ShiftedLeft(Offset);
        _array.SetArray(x);
    }
}

public static partial class BitArrayExtensions
{
    public static SizedOffsetBitArray32Ref PortionRef(ref this BitArray32 arr, int offset, int len)
    {
        return SizedOffsetBitArray32Ref.Create(ref arr, offset, len);
    }

    public static SizedOffsetBitArray32Ref PortionRef(ref this UnsizedBitArray32 arr, int offset, int len)
    {
        return new(ref arr, offset, len);
    }

    // Compute bounds
    // public static SizedOffsetBitArray32Ref PortionRef(
    //     ref this SizedOffsetBitArray32Ref arr,
    //     int offset,
    //     int len)
    // {
    //     Debug.Assert(offset >= arr.Offset);
    //     Debug.Assert(len <= arr.Length);
    //
    //     return new(ref arr, offset, len);
    // }

    public static SizedBitArray32Ref PortionRef(ref this BitArray32 arr, int len)
    {
        return SizedBitArray32Ref.Create(ref arr, len);
    }

    public static SizedBitArray32Ref PortionRef(ref this UnsizedBitArray32 arr, int len)
    {
        return new(ref arr, len);
    }
}

using System.Diagnostics;
using System.Diagnostics.Contracts;

namespace ScheduleLib.Helper;

public ref struct OffsetBitArrayForEnumRef<T>
    where T : struct, Enum
{
    static OffsetBitArrayForEnumRef()
    {
        UnsizedBitArray32.ValidateLen(_Length);
    }

    private ref UnsizedBitArray32 _impl;
    private readonly int _offset;

    public OffsetBitArrayForEnumRef(ref UnsizedBitArray32 array, int offset)
    {
        Debug.Assert(offset >= 0);

        _impl = ref array;
        _offset = offset;
    }

    public static OffsetBitArrayForEnumRef<T> Create(
        ref BitArray32 array,
        int offset)
    {
        Debug.Assert(offset + _Length <= array.Len);
        return new(ref array._array, offset);
    }

    private static int _Length => EnumMembers<T>.Count;
    public readonly int Length => _Length;

    private readonly void ValidateIndex(int i)
    {
        Debug.Assert(i >= 0 && i < Length);
    }

    private readonly int RestoreIndex(int i)
    {
        ValidateIndex(i);
        return i + _offset;
    }

    private readonly EnumBitArray<T> RestoreSlice(UnsizedBitArray32 array)
    {
        var ret = array.ShiftedRightWithDataLoss(_offset).WithShrunkLen(_Length);
        return new(ret.AsUnsized());
    }

    public void ResetArray(EnumBitArray<T> array)
    {
        _impl.ClearArray(UnsizedBitArray32.GetMask(array.Length).ShiftedLeft(_offset));
        _impl.SetArray(array.AsUnsized().ShiftedLeft(_offset));
    }

    public void SetArray(EnumBitArray<T> array)
    {
        _impl.SetArray(array.AsUnsized().ShiftedLeft(_offset));
    }

    public void SetAll(bool value = true)
    {
        if (value)
        {
            SetArray(EnumBitArray<T>.AllSet);
        }
        else
        {
            ClearArray(EnumBitArray<T>.AllSet);
        }
    }

    public void ClearArray(EnumBitArray<T> array)
    {
        _impl.ClearArray(array.AsUnsized().ShiftedLeft(_offset));
    }

    public readonly EnumBitArray<T> RestoredSlice => RestoreSlice(_impl);

    [Pure]
    private readonly int GetBitIndex(T index)
    {
        var enumOffset = EnumMembers<T>.GetOffset(index);
        return RestoreIndex(enumOffset);
    }

    public void Set(T index, bool value)
    {
        int i = GetBitIndex(index);
        _impl.Set(i, value);
    }

    public void Set(T index)
    {
        int i = GetBitIndex(index);
        _impl.Set(i);
    }

    public void Clear(T index)
    {
        int i = GetBitIndex(index);
        _impl.Clear(i);
    }

    [Pure]
    public readonly bool IsSet(T index)
    {
        int i = GetBitIndex(index);
        return _impl.IsSet(i);
    }

    [Pure]
    public readonly bool AreAllSet => RestoredSlice.AreAllSet;
    [Pure]
    public readonly bool AreNoneSet => RestoredSlice.AreNoneSet;
    [Pure]
    public readonly bool AreAnySet => !AreNoneSet;
    [Pure]
    public readonly int SetCount => RestoredSlice.SetCount;

    public void ClearAll()
    {
        var shiftedMask = UnsizedBitArray32.GetOffsetMask(_offset, Length);
        _impl.ClearArray(shiftedMask);
    }

    [Pure]
    public readonly bool IsEmpty => RestoredSlice.IsEmpty;

    public readonly EnumBitArray<T>.SetEnumValuesEnumerable SetValues() => RestoredSlice.SetValues();

    public override string ToString() => RestoredSlice.ToString();
}

public static partial class BitArrayExtensions
{
    public static OffsetBitArrayForEnumRef<T> EnumPortionRef<T>(
        this ref UnsizedBitArray32 array,
        int offset)

        where T : struct, Enum
    {
        return new(ref array, offset);
    }

    public static OffsetBitArrayForEnumRef<T> EnumPortionRef<T>(
        this ref BitArray32 array,
        int offset)

        where T : struct, Enum
    {
        return OffsetBitArrayForEnumRef<T>.Create(ref array, offset);
    }
}

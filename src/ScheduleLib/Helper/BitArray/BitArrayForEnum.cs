using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using ScheduleLib.Builders;

namespace ScheduleLib.Helper;

public record struct EnumBitArray<T>
    where T : struct, Enum
{
    static EnumBitArray()
    {
        var count = AllEnumEnumerable<T>.Count;
        UnsizedBitArray32.ValidateLength(count);
    }

    private UnsizedBitArray32 _impl;

    public EnumBitArray() : this(default)
    {
    }

    public EnumBitArray(UnsizedBitArray32 impl)
    {
        _impl = impl;
    }

    public static EnumBitArray<T> AllSet
    {
        get
        {
            var t = UnsizedBitArray32.AllSet(AllEnumEnumerable<T>.Count);
            return new(t);
        }
    }

    [UnscopedRef]
    private SizedBitArray32Ref SizedImplMut => _impl.AsFixedSizeRef(AllEnumEnumerable<T>.Count);
    [Pure]
    private readonly BitArray32 SizedImpl => _impl.WithFixedSize(AllEnumEnumerable<T>.Count);

    public void Set(T index, bool value)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        SizedImplMut.Set(offset, value);
    }

    public void Set(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        SizedImplMut.Set(offset);
    }

    public void Clear(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        SizedImplMut.Clear(offset);
    }

    [Pure]
    public readonly bool IsSet(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        return SizedImpl.IsSet(offset);
    }

    [Pure]
    public readonly EnumBitArray<T> WithSet(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        var result = SizedImpl.WithSet(offset);
        return new(result.AsUnsized());
    }

    [Pure]
    public readonly EnumBitArray<T> WithClear(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        var result = SizedImpl.WithClear(offset);
        return new(result.AsUnsized());
    }

    [Pure]
    public readonly EnumBitArray<T> Flipped
    {
        get
        {
            var result = SizedImpl.Flipped;
            return new(result.AsUnsized());
        }
    }

    [Pure]
    public readonly EnumBitArray<T> Intersect(EnumBitArray<T> other)
    {
        var result = SizedImpl.Intersect(other.SizedImpl);
        return new(result.AsUnsized());
    }

    [Pure]
    public readonly bool AreAllSet => _impl.AreAllSet(AllEnumEnumerable<T>.Count);
    [Pure]
    public readonly bool AreNoneSet => _impl.AreNoneSet;
    [Pure]
    public readonly bool AreAnySet => !_impl.AreNoneSet;
    [Pure]
    public readonly int SetCount => _impl.SetCount;

    public void ClearAll()
    {
        this = default;
    }

    [Pure]
    public readonly T? GetFirstSet()
    {
        var index = SizedImpl.GetSetAfter(-1);
        if (index == -1)
        {
            return null;
        }
        return AllEnumEnumerable<T>.EnumFromOffset(index);
    }

    [Pure]
    public readonly SetEnumValuesEnumerable SetValues() => new(_impl.Bits);

    [Pure]
    public readonly uint Bits => _impl.Bits;
    [Pure]
    public readonly bool IsEmpty => _impl.IsEmpty;

    public readonly struct SetEnumValuesEnumerable : IEnumerable<T>
    {
        private readonly SetBitIndicesEnumerable _e;

        public SetEnumValuesEnumerable(uint bits) => _e = new(bits);
        public SetEnumValuesEnumerator GetEnumerator() => new(_e.GetEnumerator());
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct SetEnumValuesEnumerator : IEnumerator<T>
    {
        private SetBitIndicesEnumerator _inner;

        public SetEnumValuesEnumerator(SetBitIndicesEnumerator x) => _inner = x;
        public bool MoveNext() => _inner.MoveNext();
        [Pure]
        public readonly T Current => AllEnumEnumerable<T>.EnumFromOffset(_inner.Current);
        [Pure]
        readonly object IEnumerator.Current => Current;

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public void Dispose() => _inner.Dispose();
    }

    [Pure]
    public readonly EnumBitArray<T> Union(EnumBitArray<T> other)
    {
        var sizedSelf = SizedImpl;
        var sizedOther = other.SizedImpl;
        var ret = sizedSelf.Union(sizedOther);
        return new(ret.AsUnsized());
    }
}

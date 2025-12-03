using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib.Helper;

public record struct BitArrayForEnum<T>
    where T : struct, Enum
{
    static BitArrayForEnum()
    {
        var count = AllEnumEnumerable<T>.Count;
        UnsizedBitArray32.ValidateLength(count);
    }

    private UnsizedBitArray32 _impl;

    public BitArrayForEnum() : this(default)
    {
    }

    internal BitArrayForEnum(UnsizedBitArray32 impl)
    {
        _impl = impl;
    }

    public static BitArrayForEnum<T> AllSet
    {
        get
        {
            var t = UnsizedBitArray32.AllSet(AllEnumEnumerable<T>.Count);
            return new(t);
        }
    }

    [UnscopedRef]
    private SizedBitArray32Ref SizedImplMut => _impl.AsFixedSizeRef(AllEnumEnumerable<T>.Count);
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

    public readonly bool IsSet(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        return SizedImpl.IsSet(offset);
    }

    public readonly BitArrayForEnum<T> WithSet(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        var result = SizedImpl.WithSet(offset);
        return new(result.AsUnsized());
    }

    public readonly BitArrayForEnum<T> WithClear(T index)
    {
        var offset = AllEnumEnumerable<T>.GetOffset(index);
        var result = SizedImpl.WithClear(offset);
        return new(result.AsUnsized());
    }

    public readonly BitArrayForEnum<T> Flipped
    {
        get
        {
            var result = SizedImpl.Flipped;
            return new(result.AsUnsized());
        }
    }

    public readonly BitArrayForEnum<T> Intersect(BitArrayForEnum<T> other)
    {
        var result = SizedImpl.Intersect(other.SizedImpl);
        return new(result.AsUnsized());
    }

    public readonly bool AreAllSet => _impl.AreAllSet(AllEnumEnumerable<T>.Count);
    public readonly bool AreNoneSet => _impl.AreNoneSet;
    public readonly int SetCount => _impl.SetCount;

    public void ClearAll()
    {
        this = default;
    }

    public readonly T? GetFirstSet()
    {
        var index = SizedImpl.GetSetAfter(-1);
        if (index == -1)
        {
            return null;
        }
        return AllEnumEnumerable<T>.EnumFromOffset(index);
    }

    public readonly SetEnumValuesEnumerable SetValues() => new(_impl.Bits);

    public readonly uint Bits => _impl.Bits;

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
        public readonly T Current => AllEnumEnumerable<T>.EnumFromOffset(_inner.Current);
        readonly object IEnumerator.Current => Current;

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public void Dispose() => _inner.Dispose();
    }
}

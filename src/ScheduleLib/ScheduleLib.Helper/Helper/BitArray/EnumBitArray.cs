using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper;

public record struct EnumBitArray<T>
    where T : struct, Enum
{
    static EnumBitArray()
    {
        UnsizedBitArray32.ValidateLen(_Length);
    }

    private UnsizedBitArray32 _impl;

    public EnumBitArray() : this(default)
    {
    }

    public EnumBitArray(UnsizedBitArray32 impl)
    {
        Debug.Assert(impl.WithShrunkLen(_Length).AsUnsized() == impl);
        _impl = impl;
    }

    public readonly UnsizedBitArray32 AsUnsized() => _impl;

    public static EnumBitArray<T> Create(BitArray32 impl)
    {
        Debug.Assert(impl.Len == _Length);
        return new(impl.AsUnsized());
    }

    public static EnumBitArray<T> Empty => new();

    public static EnumBitArray<T> AllSet
    {
        get
        {
            var t = UnsizedBitArray32.AllSet(EnumMembers<T>.Count);
            return new(t);
        }
    }

    private static int _Length => EnumMembers<T>.Count;
    [UnscopedRef]
    private SizedBitArray32Ref SizedImplMut => _impl.AsFixedSizeRef(_Length);
    [Pure]
    private readonly BitArray32 SizedImpl => _impl.WithFixedSize(_Length);

    public void Set(T index, bool value)
    {
        var offset = EnumMembers<T>.GetOffset(index);
        SizedImplMut.Set(offset, value);
    }

    public void Set(T index)
    {
        var offset = EnumMembers<T>.GetOffset(index);
        SizedImplMut.Set(offset);
    }

    public void Clear(T index)
    {
        var offset = EnumMembers<T>.GetOffset(index);
        SizedImplMut.Clear(offset);
    }

    [Pure]
    public readonly bool IsSet(T index)
    {
        var offset = EnumMembers<T>.GetOffset(index);
        return SizedImpl.IsSet(offset);
    }

    [Pure]
    public readonly EnumBitArray<T> WithSet(T index)
    {
        var offset = EnumMembers<T>.GetOffset(index);
        var result = SizedImpl.WithSet(offset);
        return new(result.AsUnsized());
    }

    [Pure]
    public readonly EnumBitArray<T> WithClear(T index)
    {
        var offset = EnumMembers<T>.GetOffset(index);
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
    public readonly EnumBitArray<T> Remove(EnumBitArray<T> other)
    {
        var result = SizedImpl.Remove(other.SizedImpl);
        return new(result.AsUnsized());
    }

    [Pure]
    public readonly bool AreAllSet => _impl.AreAllSet(_Length);
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
        return EnumMembers<T>.EnumFromOffset(index);
    }

    [Pure]
    public readonly SetEnumValuesEnumerable SetValues() => new(_impl.Bits);

    [Pure]
    public readonly uint Bits => _impl.Bits;
    [Pure]
    public readonly bool IsEmpty => _impl.IsEmpty;

    [Pure]
    public readonly int Length => _Length;

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
        public readonly T Current => EnumMembers<T>.EnumFromOffset(_inner.Current);
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

    public override string ToString()
    {
        var sb = new StringBuilder();
        var lb = new ListStringBuilder(sb);
        foreach (var value in SetValues())
        {
            lb.Append(Enum.GetName(value));
        }
        return sb.ToString();
    }
}

public static class EnumBitArrayJsonHelper
{
    public static SameGenericArgsConverterFactory ConverterFactory { get; } = new(
        objectType: typeof(EnumBitArray<>),
        converterType: typeof(EnumBitArrayJsonConverter<>));
}

public sealed class EnumBitArrayJsonConverter<T> : JsonConverter<EnumBitArray<T>>
    where T : struct, Enum
{
    public override EnumBitArray<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected an array");
        }
        var ret = new EnumBitArray<T>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            var t = JsonSerializer.Deserialize<T>(ref reader, options);
            ret.Set(t);
        }
        return ret;
    }

    public override void Write(
        Utf8JsonWriter writer,
        EnumBitArray<T> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value.SetValues())
        {
            JsonSerializer.Serialize(writer, v, options);
        }
        writer.WriteEndArray();
    }
}

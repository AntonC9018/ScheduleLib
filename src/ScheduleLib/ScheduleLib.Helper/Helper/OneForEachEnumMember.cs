using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper;

public ref struct OneForEachEnumMemberSpan<TEnum, TValue>
    where TEnum : struct, Enum
{
    public readonly Span<TValue> Storage;

    public OneForEachEnumMemberSpan(Span<TValue> storage)
    {
        Storage = storage;
    }

    public ref TValue this[TEnum e]
    {
        get
        {
            var index = EnumMembers<TEnum>.GetOffset(e);
            return ref Storage[index];
        }
    }

    public Enumerator GetEnumerator() => new(this);

    public ref struct Enumerator
    {
        private EnumMembers<TEnum>.Enumerator _e;
        private readonly OneForEachEnumMemberSpan<TEnum, TValue> _span;

        public Enumerator(OneForEachEnumMemberSpan<TEnum, TValue> span)
        {
            _e = new EnumMembers<TEnum>().GetEnumerator();
            _span = span;
        }

        public readonly ref struct Item
        {
            public readonly TEnum Key;
            public readonly ref TValue Value;

            public Item(TEnum key, ref TValue value)
            {
                Key = key;
                Value = ref value;
            }

            public void Deconstruct(out TEnum key, out TValue value)
            {
                key = Key;
                value = Value;
            }
        }

        public Item Current
        {
            get
            {
                return new(
                    _e.Current,
                    ref _span[_e.Current]);
            }
        }

        public bool MoveNext()
        {
            if (!_e.MoveNext())
            {
                return false;
            }
            return true;
        }
    }

    public void Fill(TValue val) => Storage.Fill(val);
    public void Clear() => Storage.Clear();

    public EnumMembers<TEnum> Keys => new();
    public void CopyTo(OneForEachEnumMemberSpan<TEnum, TValue> other)
    {
        Storage.CopyTo(other.Storage);
    }
}

public readonly struct MemoryItem<TEnum, TValue>
    where TEnum : struct, Enum
{
    public readonly TEnum Key;
    private readonly OneForEachEnumMemberMemory<TEnum, TValue> _mem;

    public ref TValue Value => ref _mem[Key];

    public MemoryItem(TEnum key, OneForEachEnumMemberMemory<TEnum, TValue> mem)
    {
        Key = key;
        _mem = mem;
    }

    public void Deconstruct(out TEnum lessonType, out TValue perLessonBuilder)
    {
        lessonType = Key;
        perLessonBuilder = Value;
    }
}

public readonly struct OneForEachEnumMemberMemory<TEnum, TValue>
    : IEnumerable<MemoryItem<TEnum, TValue>>
    where TEnum : struct, Enum
{
    public readonly Memory<TValue> Storage;

    public OneForEachEnumMemberMemory(Memory<TValue> storage)
    {
        Storage = storage;
    }

    public ref TValue this[TEnum e]
    {
        get
        {
            var index = EnumMembers<TEnum>.GetOffset(e);
            return ref Storage.Span[index];
        }
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<MemoryItem<TEnum, TValue>> IEnumerable<MemoryItem<TEnum, TValue>>.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<MemoryItem<TEnum, TValue>>
    {
        private EnumMembers<TEnum>.Enumerator _e;
        private readonly OneForEachEnumMemberMemory<TEnum, TValue> _mem;

        public Enumerator(OneForEachEnumMemberMemory<TEnum, TValue> mem)
        {
            _e = new EnumMembers<TEnum>().GetEnumerator();
            _mem = mem;
        }

        public void Dispose()
        {
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        object? IEnumerator.Current => Current;
        public MemoryItem<TEnum, TValue> Current => new(_e.Current, _mem);

        public bool MoveNext()
        {
            if (!_e.MoveNext())
            {
                return false;
            }
            return true;
        }
    }

    public void Fill(TValue val) => Storage.Span.Fill(val);
    public void Clear() => Storage.Span.Clear();

    public EnumMembers<TEnum> Keys => new();
}

public readonly struct RentedOneForEachEnumMemberArray<TEnum, TValue> : IDisposable
    where TEnum : struct, Enum
{
    private readonly RentedBuffer<TValue> _items;
    static RentedOneForEachEnumMemberArray() => EnumMembers<TEnum>.AssertCompiles();
    internal RentedOneForEachEnumMemberArray(RentedBuffer<TValue> items) => _items = items;
    public void Dispose() => _items.Dispose();
    public OneForEachEnumMemberSpan<TEnum, TValue> Span => new(_items.Span);
    public OneForEachEnumMemberSpan<TEnum, TValue>.Enumerator GetEnumerator() => Span.GetEnumerator();
    public ref TValue this[TEnum e] => ref Span[e];
    public void Clear() => _items.Span.Fill(default!);
}

public readonly struct OneForEachEnumMemberArrayBuilder<TEnum, TValue>()
    where TEnum : struct, Enum
{
    private readonly OneForEachEnumMemberArray<TEnum, TValue> _arr = new();
    public OneForEachEnumMemberArrayBuilder<TEnum, TValue> Set(TEnum e, TValue value)
    {
        _arr[e] = value;
        return this;
    }
    public OneForEachEnumMemberArray<TEnum, TValue> Build()
    {
        var notSet = new EnumBitArray<TEnum>();
        foreach (var x in _arr)
        {
            if (EqualityComparer<TValue>.Default.Equals(x.Value, default))
            {
                notSet.Set(x.Key);
            }
        }
        if (!notSet.IsEmpty)
        {
            throw new InvalidOperationException($"Some members not set: {notSet}");
        }
        return _arr;
    }
}

public readonly struct OneForEachEnumMemberArray<TEnum, TValue> : IEnumerable<MemoryItem<TEnum, TValue>>
    where TEnum : struct, Enum
{
    static OneForEachEnumMemberArray() => EnumMembers<TEnum>.AssertCompiles();
    private readonly TValue[] _items;
    public OneForEachEnumMemberArray() => _items = new TValue[EnumMembers<TEnum>.Count];
    internal OneForEachEnumMemberArray(TValue[] items) => _items = items;
    public TValue[] Storage => _items;
    public OneForEachEnumMemberSpan<TEnum, TValue> Span => new(_items.AsSpan());
    public OneForEachEnumMemberMemory<TEnum, TValue> Memory => new(_items.AsMemory());
    public OneForEachEnumMemberMemory<TEnum, TValue>.Enumerator GetEnumerator() => Memory.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<MemoryItem<TEnum, TValue>> IEnumerable<MemoryItem<TEnum, TValue>>.GetEnumerator() => GetEnumerator();
    public ref TValue this[TEnum e] => ref Span[e];
    public void CopyTo(OneForEachEnumMemberSpan<TEnum, TValue> other)
    {
        Storage.CopyTo(other.Storage);
    }

    [Pure]
    public TEnum FindKeyOrDefault(TValue value, TEnum defaultKey, IEqualityComparer<TValue>? comparer = null)
    {
        comparer ??= EqualityComparer<TValue>.Default;
        foreach (var x in this)
        {
            if (comparer.Equals(value, x.Value))
            {
                return x.Key;
            }
        }
        return defaultKey;
    }

    public OneForEachEnumMemberArray<TEnum, TValue> Copy() => new(Storage.ToArray());
}

public readonly struct SparseArray<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : struct, Enum
    where TValue : notnull
{
    private readonly Dictionary<TKey, TValue> _items;

    public SparseArray() : this(0)
    {
    }
    public SparseArray(int count)
    {
        _items = new(count);
    }

    public Dictionary<TKey, TValue> Storage => _items;
    public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    public void Add(TKey key, TValue value)
    {
        _items.Add(key, value);
    }

    public ref TValue? GetOrAdd(TKey key)
    {
        ref var ret = ref GetOrAdd(key, out bool e);
        _ = e;
        return ref ret;
    }

    public ref TValue? GetOrAdd(TKey key, out bool existed)
    {
        return ref CollectionsMarshal.GetValueRefOrAddDefault(_items, key, out existed);
    }

    public bool TryGet(TKey key, [NotNullWhen(true)] out TValue? value)
    {
        var ret = _items.TryGetValue(key, out value);
        return ret;
    }

    public TValue this[TKey key]
    {
        get => _items[key];
        set => _items[key] = value;
    }

    public bool IsEmpty => Storage.Count == 0;
    public int Count => Storage.Count;
    public void Clear() => Storage.Clear();
}

public static class OneForEach
{
    public struct Helper<TEnum> where TEnum : struct, Enum
    {
        static Helper() => EnumMembers<TEnum>.AssertCompiles();

        public RentedOneForEachEnumMemberArray<TEnum, TValue> RentArray<TValue>()
        {
            var len = EnumMembers<TEnum>.Count;
            var buffer = new RentedBuffer<TValue>(len);
            return new(buffer);
        }

        public OneForEachEnumMemberArray<TEnum, TValue> CreateArray<TValue>() => new();

        public SparseArray<TEnum, TValue> CreateSparseArray<TValue>(int count = 0)
            where TValue : notnull
        {
            return new(count);
        }

        public OneForEachEnumMemberArrayBuilder<TEnum, TValue> Builder<TValue>() => new();
    }
    public static Helper<TEnum> Enum<TEnum>() where TEnum : struct, Enum
    {
        return new();
    }

    public static SameGenericArgsConverterFactory RentedConverterFactory { get; } = new(
        objectType: typeof(RentedOneForEachEnumMemberArray<,>),
        converterType: typeof(RentedOneForEachEnumMemberArrayConverter<,>));
}

public sealed class RentedOneForEachEnumMemberArrayConverter<TEnum, TValue>
    : JsonConverter<RentedOneForEachEnumMemberArray<TEnum, TValue?>>
    where TEnum : struct, Enum
{
    public override RentedOneForEachEnumMemberArray<TEnum, TValue?> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected an object");
        }

        var ret = OneForEach.Enum<TEnum>().RentArray<TValue>();
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                var e = ParseEnumName(ref reader);
                reader.Read();

                var v = JsonSerializer.Deserialize<TValue>(ref reader, options);
                ret[e] = v!;
            }
        }
        catch
        {
            // May still leak if it's a property inside another object,
            // but it's undetectable from here and literally cannot be prevented.
            ret.Dispose();
            if (typeof(TValue).IsClass
                && !typeof(TValue).IsSealed
                && !typeof(TValue).IsAssignableTo(typeof(IDisposable)))
            {
                foreach (var x in ret)
                {
                    if (x.Value is IDisposable d)
                    {
                        d.Dispose();
                    }
                }
            }
            else if (typeof(TValue).IsAssignableTo(typeof(IDisposable)))
            {
                foreach (var x in ret)
                {
                    ((IDisposable?) x.Value)?.Dispose();
                }
            }
            throw;
        }
        return ret!;
    }

    public override void Write(
        Utf8JsonWriter writer,
        RentedOneForEachEnumMemberArray<TEnum, TValue?> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, val) in value)
        {
            writer.WritePropertyName(Enum.GetName(key)!);
            JsonSerializer.Serialize(writer, val, options);
        }
        writer.WriteEndObject();
    }

    private static TEnum ParseEnumName(ref Utf8JsonReader reader)
    {
        Debug.Assert(reader.TokenType == JsonTokenType.PropertyName);
        Span<char> buf = stackalloc char[128];

        int len = -1;
        try
        {
            len = reader.CopyString(buf);
        }
        catch (ArgumentException)
        {
        }

        TEnum e;
        if (len != -1)
        {
            var name = buf[.. len];
            e = Enum.Parse<TEnum>(name);
        }
        else
        {
            var name = reader.GetString();
            if (name == null)
            {
                throw new JsonException("Null key");
            }
            e = Enum.Parse<TEnum>(name);
        }
        return e;
    }
}

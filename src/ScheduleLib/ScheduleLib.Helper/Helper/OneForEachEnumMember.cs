using System.Collections;

namespace ScheduleLib.Helper;

public ref struct OneForEachEnumMemberSpan<TEnum, TValue>
    where TEnum : struct, Enum
{
    private readonly Span<TValue> _storage;

    public OneForEachEnumMemberSpan(Span<TValue> storage)
    {
        _storage = storage;
    }

    public ref TValue this[TEnum e]
    {
        get
        {
            var index = EnumMembers<TEnum>.GetOffset(e);
            return ref _storage[index];
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

    public void Fill(TValue val) => _storage.Fill(val);
    public void Clear() => _storage.Clear();

    public EnumMembers<TEnum> Keys => new();
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
    private readonly Memory<TValue> _storage;

    public OneForEachEnumMemberMemory(Memory<TValue> storage)
    {
        _storage = storage;
    }

    public ref TValue this[TEnum e]
    {
        get
        {
            var index = EnumMembers<TEnum>.GetOffset(e);
            return ref _storage.Span[index];
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

    public void Fill(TValue val) => _storage.Span.Fill(val);
    public void Clear() => _storage.Span.Clear();

    public EnumMembers<TEnum> Keys => new();
}

public readonly struct RentedOneForEachEnumMemberArray<TEnum, TValue> : IDisposable
    where TEnum : struct, Enum
{
    private readonly RentedBuffer<TValue> _items;

    internal RentedOneForEachEnumMemberArray(RentedBuffer<TValue> items) => _items = items;
    public void Dispose() => _items.Dispose();

    public OneForEachEnumMemberSpan<TEnum, TValue> Span => new(_items.Span);
    public OneForEachEnumMemberSpan<TEnum, TValue>.Enumerator GetEnumerator() => Span.GetEnumerator();
    public ref TValue this[TEnum e] => ref Span[e];
}

public readonly struct OneForEachEnumMemberArray<TEnum, TValue> : IEnumerable<MemoryItem<TEnum, TValue>>
    where TEnum : struct, Enum
{
    private readonly TValue[] _items;

    internal OneForEachEnumMemberArray(TValue[] items) => _items = items;
    public OneForEachEnumMemberSpan<TEnum, TValue> Span => new(_items.AsSpan());
    public OneForEachEnumMemberMemory<TEnum, TValue> Memory => new(_items.AsMemory());
    public OneForEachEnumMemberMemory<TEnum, TValue>.Enumerator GetEnumerator() => Memory.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    IEnumerator<MemoryItem<TEnum, TValue>> IEnumerable<MemoryItem<TEnum, TValue>>.GetEnumerator() => GetEnumerator();
    public ref TValue this[TEnum e] => ref Span[e];
}

public static class OneForEach
{
    public struct Helper<TEnum> where TEnum : struct, Enum
    {
        public RentedOneForEachEnumMemberArray<TEnum, TValue> RentArray<TValue>()
        {
            var len = EnumMembers<TEnum>.Count;
            var buffer = new RentedBuffer<TValue>(len);
            return new(buffer);
        }

        public OneForEachEnumMemberArray<TEnum, TValue> CreateArray<TValue>()
        {
            var len = EnumMembers<TEnum>.Count;
            var buffer = new TValue[len];
            return new(buffer);
        }
    }
    public static Helper<TEnum> Enum<TEnum>() where TEnum : struct, Enum
    {
        return new();
    }
}

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

            public void Deconstruct(out TEnum lessonType, out TValue perLessonBuilder)
            {
                lessonType = Key;
                perLessonBuilder = Value;
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

public readonly struct RentedOneForEachEnumMemberArray<TEnum, TValue>
    : IDisposable
    where TEnum : struct, Enum
{
    private readonly RentedBuffer<TValue> _items;

    internal RentedOneForEachEnumMemberArray(RentedBuffer<TValue> items) => _items = items;
    public void Dispose() => _items.Dispose();

    public OneForEachEnumMemberSpan<TEnum, TValue> Span => new(_items.Span);
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
    }
    public static Helper<TEnum> Enum<TEnum>() where TEnum : struct, Enum
    {
        return new();
    }
}

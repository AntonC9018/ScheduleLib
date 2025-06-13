using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScheduleLib.Helper;

public readonly record struct SizedItem<T>
{
    public readonly T Item;
    public readonly int Size;

    public SizedItem(T item, int size)
    {
        Debug.Assert(size >= 0, "Size must be non-negative");
        Item = item;
        Size = size;
    }

    public SizedItem<T> WithSize(int newSize) => new(Item, newSize);
    public SizedItem<T> WithItem(T newItem) => new(newItem, Size);
}

public enum ReplaceItemStatus
{
    DidNothing,
    FullyReplaced,
    PartlyReplaced,
    Spliced,
}

public readonly struct SizedItemArray<T>
{
    private readonly List<SizedItem<T>> _items;

    public SizedItemArray() : this(0)
    {
    }

    public SizedItemArray(int cap)
    {
        _items = new(cap);
    }

    public void Clear()
    {
        _items.Clear();
    }

    public int Count => _items.Count;
    public bool IsEmpty => Count == 0;

    private (int Index, int StartIndex)? FindPosition(int colIndex)
    {
        Debug.Assert(colIndex >= 0);

        int a = 0;
        for (int index = 0; index < _items.Count; index++)
        {
            var g = _items[index];
            int nextItemStartIndex = a + g.Size;
            if (nextItemStartIndex > colIndex)
            {
                return (index, a);
            }
            a = nextItemStartIndex;
        }

        return null;
    }

    public T Find(int colIndex)
    {
        if (FindPosition(colIndex) is not { } i)
        {
            throw new ArgumentOutOfRangeException(nameof(colIndex));
        }
        return _items[i.Index].Item;
    }

    public int? FindPosition(T item)
    {
        int colIndex = 0;
        for (int index = 0; index < _items.Count; index++)
        {
            var it = _items[index];
            if (EqualityComparer<T>.Default.Equals(it.Item, item))
            {
                return colIndex;
            }
            colIndex += it.Size;
        }
        return null;
    }

    public void Add(SizedItem<T> it)
    {
        _items.Add(it);
    }

    public void AddAt(int position, SizedItem<T> it, T? fillerValue = default)
    {
        var totalSize = TotalSize;
        if (position < totalSize)
        {
            throw new ArgumentException("Must be after the last element", nameof(position));
        }

        var diff = position - totalSize;
        if (diff > 0)
        {
            _items.Add(new(fillerValue!, diff));
        }

        _items.Add(it);
    }

    public bool Replace(T oldItem, T newItem)
    {
        if (FindPosition(oldItem) is { } index)
        {
            return ReplaceItem(index, newItem);
        }
        return false;
    }

    public bool ReplaceItem(int colIndex, T newItem)
    {
        if (FindPosition(colIndex) is not { } e)
        {
            return false;
        }
        ref var x = ref CollectionsMarshal.AsSpan(_items)[e.Index];
        x = x.WithItem(newItem);
        return true;
    }

    public ReplaceItemStatus ReplaceAt(
        int colIndex,
        SizedItem<T> item)
    {
        return ReplaceAtRange(colIndex, [item]);
    }

    // Disallows growth.
    // Throws if the new items don't fit in the indicated item's size.
    public ReplaceItemStatus ReplaceAtRange(
        int colIndex,
        ReadOnlySpan<SizedItem<T>> items)
    {
        if (items.Length == 0)
        {
            return ReplaceItemStatus.DidNothing;
        }
        if (FindPosition(colIndex) is not { } existing)
        {
            throw new ArgumentOutOfRangeException(nameof(colIndex), "Must be an actual item in the array");
        }

        ref var existingItemRef = ref CollectionsMarshal.AsSpan(_items)[existing.Index];
        int existingItemSize = existingItemRef.Size;
        int sizeOfItems = SizeOfItems(items);
        int offset = colIndex - existing.StartIndex;
        int availableSize = existingItemSize - offset;

        if (availableSize < sizeOfItems)
        {
            throw new ArgumentException("The indicated item cannot hold all the items", nameof(items));
        }

        // Equivalent check: offset == 0 && availableSize == sizeOfItems
        if (existingItemSize == sizeOfItems)
        {
            existingItemRef = items[0];
            _items.InsertRange(existing.Index + 1, items[1 ..]);
            return ReplaceItemStatus.FullyReplaced;
        }

        int remainingSizeEnd = availableSize - sizeOfItems;
        if (offset > 0 && remainingSizeEnd > 0)
        {
            existingItemRef = existingItemRef.WithSize(offset);
            var newItem = existingItemRef.WithSize(remainingSizeEnd);

            // Just trying to insert in one operation.
            // using var tempBuffer = new RentedBuffer<SizedItem<T>>(items.Length + 1);
            // var s = tempBuffer.Span;
            // items.CopyTo(s[.. ^1]);
            // s[^1] = existingItemRef.WithSize(remainingSizeEnd);
            // _items.InsertRange(existing.Index + 1, s);
            int x = existing.Index + 1;
            _items.InsertRange(x, items);
            x += items.Length;
            _items.Insert(x, newItem);

            return ReplaceItemStatus.Spliced;
        }

        if (offset == 0 && remainingSizeEnd > 0)
        {
            existingItemRef = existingItemRef.WithSize(remainingSizeEnd);
            _items.InsertRange(existing.Index, items);
            return ReplaceItemStatus.PartlyReplaced;
        }

        if (offset > 0 && remainingSizeEnd == 0)
        {
            existingItemRef = existingItemRef.WithSize(offset);
            _items.InsertRange(existing.Index + 1, items);
            return ReplaceItemStatus.PartlyReplaced;
        }

        throw Unreachable();

        static int SizeOfItems(ReadOnlySpan<SizedItem<T>> items)
        {
            int s = 0;
            foreach (var it in items)
            {
                s += it.Size;
            }
            return s;
        }
    }

    public int TotalSize
    {
        get
        {
            int ret = 0;
            foreach (var item in _items)
            {
                ret += item.Size;
            }
            return ret;
        }
    }

    public Enumerator GetEnumerator() => new(this);
    public WithPositionEnumerable EnumerateWithPosition() => new(this);

    public struct Enumerator
    {
        private List<SizedItem<T>>.Enumerator _enumerator;

        public Enumerator(SizedItemArray<T> arr)
        {
            _enumerator = arr._items.GetEnumerator();
        }

        public SizedItem<T> Current => _enumerator.Current;
        public bool MoveNext() => _enumerator.MoveNext();
    }

    public struct WithPositionEnumerable
    {
        private readonly SizedItemArray<T> _arr;

        public WithPositionEnumerable(SizedItemArray<T> arr)
        {
            _arr = arr;
        }

        public WithPositionEnumerator GetEnumerator() => new(_arr);
    }
    public struct WithPositionEnumerator
    {
        private Enumerator _enumerator;
        private int _accum;

        public WithPositionEnumerator(SizedItemArray<T> arr)
        {
            _enumerator = arr.GetEnumerator();
            _accum = 0;
        }

        public SizedItemWithPosition<T> Current => new(_enumerator.Current, _accum);

        public bool MoveNext()
        {
            _accum += Current.Size;
            return _enumerator.MoveNext();
        }
    }
}

public readonly record struct SizedItemWithPosition<T>(SizedItem<T> SizedItem, int Position)
{
    public readonly T Item => SizedItem.Item;
    public readonly int Size => SizedItem.Size;
}
